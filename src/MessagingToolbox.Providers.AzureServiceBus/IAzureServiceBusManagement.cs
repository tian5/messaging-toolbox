using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// Management-plane operations for Azure Service Bus. Cloud namespaces use the regular AMQP
/// connection string; the emulator uses the derived <c>:5300</c> admin endpoint.
/// </summary>
public interface IAzureServiceBusManagement
{
    /// <summary>
    /// Checks that the management endpoint answers. Completes when it does (an empty namespace
    /// counts as reachable); otherwise throws a translated <c>MessagingProviderException</c>.
    /// </summary>
    Task ProbeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MessagingEntity>> ListEntitiesAsync(CancellationToken cancellationToken);

    Task<EntityRuntimeInfo> GetQueueRuntimeInfoAsync(string queueName, CancellationToken cancellationToken);

    Task<EntityRuntimeInfo> GetTopicRuntimeInfoAsync(string topicName, CancellationToken cancellationToken);

    Task<EntityRuntimeInfo> GetSubscriptionRuntimeInfoAsync(string topicName, string subscriptionName, CancellationToken cancellationToken);

    Task<MessagingEntity> CreateOrUpdateQueueAsync(string queueName, IReadOnlyDictionary<string, object?> options, CancellationToken cancellationToken);

    Task DeleteQueueAsync(string queueName, CancellationToken cancellationToken);

    Task<MessagingEntity> CreateOrUpdateTopicAsync(string topicName, IReadOnlyDictionary<string, object?> options, CancellationToken cancellationToken);

    Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken);

    Task<MessagingEntity> CreateOrUpdateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyDictionary<string, object?> options, CancellationToken cancellationToken);

    Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken);

    Task<EntityDefinition> GetQueueDefinitionAsync(string queueName, CancellationToken cancellationToken);

    Task<EntityDefinition> GetTopicDefinitionAsync(string topicName, CancellationToken cancellationToken);

    Task<EntityDefinition> GetSubscriptionDefinitionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken);
}
