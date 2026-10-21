using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.AzureServiceBus.Errors;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// Wrapper over <see cref="ServiceBusAdministrationClient"/> that serves cloud namespaces and the
/// Azure Service Bus emulator (via the <c>:5300</c> admin endpoint). Entity CRUD and runtime
/// information are surfaced through broker-agnostic types; SDK exceptions are translated at the
/// boundary.
/// </summary>
public sealed class AzureServiceBusManagement : IAzureServiceBusManagement
{
    private readonly ServiceBusAdministrationClient _administrationClient;
    private readonly ILogger<AzureServiceBusManagement>? _logger;
    private readonly string _endpoint;

    public AzureServiceBusManagement(string connectionString, ILogger<AzureServiceBusManagement>? logger = null)
    {
        _administrationClient = new ServiceBusAdministrationClient(connectionString);
        _logger = logger;
        _endpoint = ServiceBusConnectionStringProperties.Parse(connectionString).Endpoint.Authority;
    }

    /// <summary>
    /// Probes the administration endpoint by reading the first page of queues. Any answer counts —
    /// a fresh emulator with no entities is exactly when management is needed. Failures are thrown
    /// translated, so the caller can show why management is unavailable.
    /// </summary>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using IAsyncEnumerator<QueueProperties> queueEnumerator =
                _administrationClient.GetQueuesAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

            await queueEnumerator.MoveNextAsync();
        }
        catch (HttpRequestException exception)
        {
            throw new MessagingProviderUnavailableException(
                $"The admin endpoint {_endpoint} could not be reached — the emulator may still be starting, or the endpoint isn't exposed to the toolbox.", exception);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, _endpoint);
        }
        catch (RequestFailedException exception)
        {
            throw new MessagingProviderException(
                $"The management endpoint {_endpoint} answered with HTTP {exception.Status} — is it an Azure Service Bus emulator 2.0 or later?", exception);
        }
    }

    public async Task<IReadOnlyList<MessagingEntity>> ListEntitiesAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, QueueRuntimeProperties> queueRuntime = await CollectByNameAsync(
            _administrationClient.GetQueuesRuntimePropertiesAsync(cancellationToken),
            properties => properties.Name);
        Dictionary<string, TopicRuntimeProperties> topicRuntime = await CollectByNameAsync(
            _administrationClient.GetTopicsRuntimePropertiesAsync(cancellationToken),
            properties => properties.Name);

        List<MessagingEntity> entities = [];

        await foreach (QueueProperties queue in _administrationClient.GetQueuesAsync(cancellationToken))
        {
            queueRuntime.TryGetValue(queue.Name, out QueueRuntimeProperties? runtime);
            entities.Add(new MessagingEntity
            {
                Path = queue.Name,
                Name = queue.Name,
                Kind = EntityKind.Queue,
                Status = MapStatus(queue.Status),
                RequiresSession = queue.RequiresSession,
                RuntimeInfo = runtime is null ? null : MapQueueRuntimeInfo(runtime),
            });
        }

        await foreach (TopicProperties topic in _administrationClient.GetTopicsAsync(cancellationToken))
        {
            topicRuntime.TryGetValue(topic.Name, out TopicRuntimeProperties? runtime);
            entities.Add(new MessagingEntity
            {
                Path = topic.Name,
                Name = topic.Name,
                Kind = EntityKind.Topic,
                Status = MapStatus(topic.Status),
                RuntimeInfo = runtime is null ? null : MapTopicRuntimeInfo(runtime),
            });

            Dictionary<string, SubscriptionRuntimeProperties> subscriptionRuntime = await CollectByNameAsync(
                _administrationClient.GetSubscriptionsRuntimePropertiesAsync(topic.Name, cancellationToken),
                properties => properties.SubscriptionName);

            await foreach (SubscriptionProperties subscription in _administrationClient.GetSubscriptionsAsync(topic.Name, cancellationToken))
            {
                subscriptionRuntime.TryGetValue(subscription.SubscriptionName, out SubscriptionRuntimeProperties? subRuntime);
                entities.Add(new MessagingEntity
                {
                    Path = $"{topic.Name}/subscriptions/{subscription.SubscriptionName}",
                    Name = subscription.SubscriptionName,
                    Kind = EntityKind.Subscription,
                    ParentPath = topic.Name,
                    Status = MapStatus(subscription.Status),
                    RequiresSession = subscription.RequiresSession,
                    RuntimeInfo = subRuntime is null ? null : MapSubscriptionRuntimeInfo(subRuntime),
                });
            }
        }

        return entities;
    }

    public async Task<EntityRuntimeInfo> GetQueueRuntimeInfoAsync(string queueName, CancellationToken cancellationToken)
    {
        try
        {
            QueueRuntimeProperties runtime = await _administrationClient.GetQueueRuntimePropertiesAsync(queueName, cancellationToken);
            return MapQueueRuntimeInfo(runtime);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, queueName);
        }
    }

    public async Task<EntityRuntimeInfo> GetTopicRuntimeInfoAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            TopicRuntimeProperties runtime = await _administrationClient.GetTopicRuntimePropertiesAsync(topicName, cancellationToken);
            return MapTopicRuntimeInfo(runtime);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, topicName);
        }
    }

    public async Task<EntityRuntimeInfo> GetSubscriptionRuntimeInfoAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        string entityPath = $"{topicName}/subscriptions/{subscriptionName}";
        try
        {
            SubscriptionRuntimeProperties runtime = await _administrationClient.GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName, cancellationToken);
            return MapSubscriptionRuntimeInfo(runtime);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task<MessagingEntity> CreateOrUpdateQueueAsync(string queueName, IReadOnlyDictionary<string, object?> options, CancellationToken cancellationToken)
    {
        try
        {
            Response<QueueProperties>? existing = await _administrationClient.GetQueueAsync(queueName, cancellationToken);
            QueueProperties properties = existing.Value;
            ApplyQueueOptions(properties, options);
            Response<QueueProperties> updated = await _administrationClient.UpdateQueueAsync(properties, cancellationToken);
            return MapQueue(updated.Value);
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            CreateQueueOptions createOptions = new(queueName);
            ApplyQueueOptions(createOptions, options);
            Response<QueueProperties> created = await _administrationClient.CreateQueueAsync(createOptions, cancellationToken);
            return MapQueue(created.Value);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, queueName);
        }
    }

    public async Task DeleteQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationClient.DeleteQueueAsync(queueName, cancellationToken);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, queueName);
        }
    }

    public async Task<MessagingEntity> CreateOrUpdateTopicAsync(string topicName, IReadOnlyDictionary<string, object?> options, CancellationToken cancellationToken)
    {
        try
        {
            Response<TopicProperties>? existing = await _administrationClient.GetTopicAsync(topicName, cancellationToken);
            TopicProperties properties = existing.Value;
            ApplyTopicOptions(properties, options);
            Response<TopicProperties> updated = await _administrationClient.UpdateTopicAsync(properties, cancellationToken);
            return MapTopic(updated.Value);
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            CreateTopicOptions createOptions = new(topicName);
            ApplyTopicOptions(createOptions, options);
            Response<TopicProperties> created = await _administrationClient.CreateTopicAsync(createOptions, cancellationToken);
            return MapTopic(created.Value);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, topicName);
        }
    }

    public async Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationClient.DeleteTopicAsync(topicName, cancellationToken);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, topicName);
        }
    }

    public async Task<MessagingEntity> CreateOrUpdateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyDictionary<string, object?> options, CancellationToken cancellationToken)
    {
        string entityPath = $"{topicName}/subscriptions/{subscriptionName}";
        try
        {
            Response<SubscriptionProperties>? existing = await _administrationClient.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
            SubscriptionProperties properties = existing.Value;
            ApplySubscriptionOptions(properties, options);
            Response<SubscriptionProperties> updated = await _administrationClient.UpdateSubscriptionAsync(properties, cancellationToken);
            return MapSubscription(updated.Value);
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            CreateSubscriptionOptions createOptions = new(topicName, subscriptionName);
            ApplySubscriptionOptions(createOptions, options);
            Response<SubscriptionProperties> created = await _administrationClient.CreateSubscriptionAsync(createOptions, cancellationToken);
            return MapSubscription(created.Value);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        string entityPath = $"{topicName}/subscriptions/{subscriptionName}";
        try
        {
            await _administrationClient.DeleteSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task<EntityDefinition> GetQueueDefinitionAsync(string queueName, CancellationToken cancellationToken)
    {
        try
        {
            Response<QueueProperties> response = await _administrationClient.GetQueueAsync(queueName, cancellationToken);
            QueueProperties properties = response.Value;
            return new EntityDefinition(
                queueName,
                EntityKind.Queue,
                null,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["LockDuration"] = properties.LockDuration,
                    ["DefaultMessageTimeToLive"] = properties.DefaultMessageTimeToLive,
                    ["MaxSizeInMegabytes"] = properties.MaxSizeInMegabytes,
                    ["RequiresSession"] = properties.RequiresSession,
                    ["EnableDeadLetteringOnMessageExpiration"] = properties.DeadLetteringOnMessageExpiration,
                    ["ForwardTo"] = properties.ForwardTo,
                    ["AutoDeleteOnIdle"] = properties.AutoDeleteOnIdle,
                    ["EnablePartitioning"] = properties.EnablePartitioning,
                    ["DuplicateDetectionHistoryTimeWindow"] = properties.DuplicateDetectionHistoryTimeWindow,
                });
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, queueName);
        }
    }

    public async Task<EntityDefinition> GetTopicDefinitionAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            Response<TopicProperties> response = await _administrationClient.GetTopicAsync(topicName, cancellationToken);
            TopicProperties properties = response.Value;
            return new EntityDefinition(
                topicName,
                EntityKind.Topic,
                null,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["DefaultMessageTimeToLive"] = properties.DefaultMessageTimeToLive,
                    ["MaxSizeInMegabytes"] = properties.MaxSizeInMegabytes,
                    ["AutoDeleteOnIdle"] = properties.AutoDeleteOnIdle,
                    ["EnablePartitioning"] = properties.EnablePartitioning,
                    ["DuplicateDetectionHistoryTimeWindow"] = properties.DuplicateDetectionHistoryTimeWindow,
                });
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, topicName);
        }
    }

    public async Task<EntityDefinition> GetSubscriptionDefinitionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        string entityPath = $"{topicName}/subscriptions/{subscriptionName}";
        try
        {
            Response<SubscriptionProperties> response = await _administrationClient.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
            SubscriptionProperties properties = response.Value;
            return new EntityDefinition(
                entityPath,
                EntityKind.Subscription,
                topicName,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["LockDuration"] = properties.LockDuration,
                    ["DefaultMessageTimeToLive"] = properties.DefaultMessageTimeToLive,
                    ["RequiresSession"] = properties.RequiresSession,
                    ["EnableDeadLetteringOnMessageExpiration"] = properties.DeadLetteringOnMessageExpiration,
                    ["ForwardTo"] = properties.ForwardTo,
                    ["AutoDeleteOnIdle"] = properties.AutoDeleteOnIdle,
                });
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
    }

    private static async Task<Dictionary<string, TValue>> CollectByNameAsync<TValue>(AsyncPageable<TValue> pageable, Func<TValue, string> name)
        where TValue : notnull
    {
        Dictionary<string, TValue> result = new(StringComparer.Ordinal);
        await foreach (TValue item in pageable)
        {
            result[name(item)] = item;
        }

        return result;
    }

    private static EntityRuntimeInfo MapQueueRuntimeInfo(QueueRuntimeProperties runtime) => new()
    {
        ActiveMessageCount = runtime.ActiveMessageCount,
        ScheduledMessageCount = runtime.ScheduledMessageCount,
        DeadLetterMessageCount = runtime.DeadLetterMessageCount,
        TransferDeadLetterMessageCount = runtime.TransferDeadLetterMessageCount,
        SizeInBytes = runtime.SizeInBytes,
        CreatedAt = runtime.CreatedAt,
        UpdatedAt = runtime.UpdatedAt,
        AccessedAt = runtime.AccessedAt,
    };

    private static EntityRuntimeInfo MapTopicRuntimeInfo(TopicRuntimeProperties runtime) => new()
    {
        ScheduledMessageCount = runtime.ScheduledMessageCount,
        SizeInBytes = runtime.SizeInBytes,
        CreatedAt = runtime.CreatedAt,
        UpdatedAt = runtime.UpdatedAt,
        AccessedAt = runtime.AccessedAt,
    };

    private static EntityRuntimeInfo MapSubscriptionRuntimeInfo(SubscriptionRuntimeProperties runtime) => new()
    {
        ActiveMessageCount = runtime.ActiveMessageCount,
        DeadLetterMessageCount = runtime.DeadLetterMessageCount,
        TransferDeadLetterMessageCount = runtime.TransferDeadLetterMessageCount,
        CreatedAt = runtime.CreatedAt,
        UpdatedAt = runtime.UpdatedAt,
        AccessedAt = runtime.AccessedAt,
    };

    private static MessagingEntity MapQueue(QueueProperties queue) => new()
    {
        Path = queue.Name,
        Name = queue.Name,
        Kind = EntityKind.Queue,
        Status = MapStatus(queue.Status),
        RequiresSession = queue.RequiresSession,
    };

    private static MessagingEntity MapTopic(TopicProperties topic) => new()
    {
        Path = topic.Name,
        Name = topic.Name,
        Kind = EntityKind.Topic,
        Status = MapStatus(topic.Status),
    };

    private static MessagingEntity MapSubscription(SubscriptionProperties subscription) => new()
    {
        Path = $"{subscription.TopicName}/subscriptions/{subscription.SubscriptionName}",
        Name = subscription.SubscriptionName,
        Kind = EntityKind.Subscription,
        ParentPath = subscription.TopicName,
        Status = MapStatus(subscription.Status),
        RequiresSession = subscription.RequiresSession,
    };

    // EntityStatus is an extensible-enum struct — compare against its static values, no switch constants.
    private static MessagingEntityStatus MapStatus(EntityStatus status)
    {
        if (status == EntityStatus.Active)
        {
            return MessagingEntityStatus.Active;
        }

        if (status == EntityStatus.SendDisabled)
        {
            return MessagingEntityStatus.SendDisabled;
        }

        if (status == EntityStatus.ReceiveDisabled)
        {
            return MessagingEntityStatus.ReceiveDisabled;
        }

        if (status == EntityStatus.Disabled)
        {
            return MessagingEntityStatus.Disabled;
        }

        return MessagingEntityStatus.Unknown;
    }

    private void ApplyQueueOptions(QueueProperties properties, IReadOnlyDictionary<string, object?> options)
    {
        foreach ((string key, object? value) in options)
        {
            if (value is null)
            {
                continue;
            }

            switch (key)
            {
                case "LockDuration" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.LockDuration = ts;
                    break;
                case "DefaultMessageTimeToLive" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.DefaultMessageTimeToLive = ts;
                    break;
                case "MaxSizeInMegabytes" when TryGetLong(value, out long maxSize):
                    properties.MaxSizeInMegabytes = maxSize;
                    break;
                case "EnableDeadLetteringOnMessageExpiration" when value is bool enableDlq:
                    properties.DeadLetteringOnMessageExpiration = enableDlq;
                    break;
                case "ForwardTo" when value is string forwardTo:
                    properties.ForwardTo = forwardTo;
                    break;
                case "AutoDeleteOnIdle" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.AutoDeleteOnIdle = ts;
                    break;
                case "DuplicateDetectionHistoryTimeWindow" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.DuplicateDetectionHistoryTimeWindow = ts;
                    break;
                case "RequiresSession":
                case "EnablePartitioning":
                    _logger?.LogDebug("Queue option '{OptionKey}' cannot be changed on an existing queue; ignoring.", key);
                    break;
                default:
                    _logger?.LogDebug("Ignoring unknown queue option '{OptionKey}'.", key);
                    break;
            }
        }
    }

    private void ApplyQueueOptions(CreateQueueOptions options, IReadOnlyDictionary<string, object?> optionsDictionary)
    {
        foreach ((string key, object? value) in optionsDictionary)
        {
            if (value is null)
            {
                continue;
            }

            switch (key)
            {
                case "LockDuration" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.LockDuration = ts;
                    break;
                case "DefaultMessageTimeToLive" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.DefaultMessageTimeToLive = ts;
                    break;
                case "MaxSizeInMegabytes" when TryGetLong(value, out long maxSize):
                    options.MaxSizeInMegabytes = maxSize;
                    break;
                case "RequiresSession" when value is bool requiresSession:
                    options.RequiresSession = requiresSession;
                    break;
                case "EnableDeadLetteringOnMessageExpiration" when value is bool enableDlq:
                    options.DeadLetteringOnMessageExpiration = enableDlq;
                    break;
                case "ForwardTo" when value is string forwardTo:
                    options.ForwardTo = forwardTo;
                    break;
                case "AutoDeleteOnIdle" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.AutoDeleteOnIdle = ts;
                    break;
                case "EnablePartitioning" when value is bool enablePartitioning:
                    options.EnablePartitioning = enablePartitioning;
                    break;
                case "DuplicateDetectionHistoryTimeWindow" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.DuplicateDetectionHistoryTimeWindow = ts;
                    break;
                default:
                    _logger?.LogDebug("Ignoring unknown queue option '{OptionKey}'.", key);
                    break;
            }
        }
    }

    private void ApplyTopicOptions(TopicProperties properties, IReadOnlyDictionary<string, object?> options)
    {
        foreach ((string key, object? value) in options)
        {
            if (value is null)
            {
                continue;
            }

            switch (key)
            {
                case "DefaultMessageTimeToLive" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.DefaultMessageTimeToLive = ts;
                    break;
                case "MaxSizeInMegabytes" when TryGetLong(value, out long maxSize):
                    properties.MaxSizeInMegabytes = maxSize;
                    break;
                case "AutoDeleteOnIdle" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.AutoDeleteOnIdle = ts;
                    break;
                case "EnablePartitioning" when value is bool enablePartitioning:
                    properties.EnablePartitioning = enablePartitioning;
                    break;
                case "DuplicateDetectionHistoryTimeWindow" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.DuplicateDetectionHistoryTimeWindow = ts;
                    break;
                default:
                    _logger?.LogDebug("Ignoring unknown topic option '{OptionKey}'.", key);
                    break;
            }
        }
    }

    private void ApplyTopicOptions(CreateTopicOptions options, IReadOnlyDictionary<string, object?> optionsDictionary)
    {
        foreach ((string key, object? value) in optionsDictionary)
        {
            if (value is null)
            {
                continue;
            }

            switch (key)
            {
                case "DefaultMessageTimeToLive" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.DefaultMessageTimeToLive = ts;
                    break;
                case "MaxSizeInMegabytes" when TryGetLong(value, out long maxSize):
                    options.MaxSizeInMegabytes = maxSize;
                    break;
                case "AutoDeleteOnIdle" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.AutoDeleteOnIdle = ts;
                    break;
                case "EnablePartitioning" when value is bool enablePartitioning:
                    options.EnablePartitioning = enablePartitioning;
                    break;
                case "DuplicateDetectionHistoryTimeWindow" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.DuplicateDetectionHistoryTimeWindow = ts;
                    break;
                default:
                    _logger?.LogDebug("Ignoring unknown topic option '{OptionKey}'.", key);
                    break;
            }
        }
    }

    private void ApplySubscriptionOptions(SubscriptionProperties properties, IReadOnlyDictionary<string, object?> options)
    {
        foreach ((string key, object? value) in options)
        {
            if (value is null)
            {
                continue;
            }

            switch (key)
            {
                case "LockDuration" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.LockDuration = ts;
                    break;
                case "DefaultMessageTimeToLive" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.DefaultMessageTimeToLive = ts;
                    break;
                case "RequiresSession" when value is bool requiresSession:
                    properties.RequiresSession = requiresSession;
                    break;
                case "EnableDeadLetteringOnMessageExpiration" when value is bool enableDlq:
                    properties.DeadLetteringOnMessageExpiration = enableDlq;
                    break;
                case "ForwardTo" when value is string forwardTo:
                    properties.ForwardTo = forwardTo;
                    break;
                case "AutoDeleteOnIdle" when TryGetTimeSpan(value, out TimeSpan ts):
                    properties.AutoDeleteOnIdle = ts;
                    break;
                default:
                    _logger?.LogDebug("Ignoring unknown subscription option '{OptionKey}'.", key);
                    break;
            }
        }
    }

    private void ApplySubscriptionOptions(CreateSubscriptionOptions options, IReadOnlyDictionary<string, object?> optionsDictionary)
    {
        foreach ((string key, object? value) in optionsDictionary)
        {
            if (value is null)
            {
                continue;
            }

            switch (key)
            {
                case "LockDuration" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.LockDuration = ts;
                    break;
                case "DefaultMessageTimeToLive" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.DefaultMessageTimeToLive = ts;
                    break;
                case "RequiresSession" when value is bool requiresSession:
                    options.RequiresSession = requiresSession;
                    break;
                case "EnableDeadLetteringOnMessageExpiration" when value is bool enableDlq:
                    options.DeadLetteringOnMessageExpiration = enableDlq;
                    break;
                case "ForwardTo" when value is string forwardTo:
                    options.ForwardTo = forwardTo;
                    break;
                case "AutoDeleteOnIdle" when TryGetTimeSpan(value, out TimeSpan ts):
                    options.AutoDeleteOnIdle = ts;
                    break;
                default:
                    _logger?.LogDebug("Ignoring unknown subscription option '{OptionKey}'.", key);
                    break;
            }
        }
    }

    private static bool TryGetTimeSpan(object value, out TimeSpan result)
    {
        if (value is TimeSpan ts)
        {
            result = ts;
            return true;
        }

        if (value is string s && TimeSpan.TryParse(s, out TimeSpan parsed))
        {
            result = parsed;
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryGetLong(object value, out long result)
    {
        if (value is long l)
        {
            result = l;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is string s && long.TryParse(s, out long parsed))
        {
            result = parsed;
            return true;
        }

        result = default;
        return false;
    }
}
