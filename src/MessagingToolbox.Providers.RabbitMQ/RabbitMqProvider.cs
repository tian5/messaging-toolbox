using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Errors;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// <see cref="IMessagingProvider"/> over RabbitMQ (AMQP 0-9-1) for one vhost. Discovery and counts
/// come from the management HTTP API, with config-listed queues as the fallback when it is
/// unreachable. Classic and quorum queues have no read-only browse, so their peek takes messages
/// and requeues them (<see cref="PeekMode.Requeue"/>, ADR 18); streams are read by offset.
/// </summary>
public sealed class RabbitMqProvider : IMessagingProvider
{
    private const int MaxMessagesPerCall = 5000;
    private const string ClientName = "MessagingToolbox";

    // Marks a resubmitted clone with the original's id, like the Azure Service Bus provider.
    private const string ReplayFromProperty = "toolbox-replayed-from";

    // Management probe: success is final, a failure is retried after the interval on the next use,
    // so discovery appears once a slow-starting broker is up (same pattern as the ASB emulator).
    internal static readonly TimeSpan ManagementReprobeInterval = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ManagementProbeTimeout = TimeSpan.FromSeconds(5);

    // A stream peek ends at the requested count or once no message arrived for this long.
    private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(1);

    private readonly ConnectionFactory _connectionFactory;
    private readonly IRabbitMqManagementClient _management;
    private readonly IReadOnlyList<string> _configuredQueues;
    private readonly ILogger<RabbitMqProvider> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _disposalTokenSource = new();

    // Peeks and receives on one queue are serialized: two concurrent requeue-peeks would each take
    // part of the queue and both report an incomplete view.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queueGates = new(StringComparer.Ordinal);
    // A stream is an append-only log: it can be read from any offset, but not consumed away.
    private static readonly EntityOperations StreamOperations = new()
    {
        CanReceive = false,
        CanReceiveSettle = false,
        CanPurge = false,
        Reason = "Streams are append-only logs: messages can't be received, settled or purged — peek reads them without side effects.",
    };

    private readonly ConcurrentDictionary<string, bool> _streamQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _deadLetterExchanges = new(StringComparer.Ordinal);
    private readonly HeldDeliveries _held;

    private readonly Lock _probeLock = new();
    private ProviderCapabilities _capabilities = RabbitMqCapabilities.Default;
    private bool _managementAvailable;
    private Task? _probeTask;
    private DateTimeOffset _lastProbeCompletedAt;
    private IConnection? _connection;
    private int _disposed;

    internal RabbitMqProvider(
        RabbitMqConnectionSettings settings,
        IRabbitMqManagementClient management,
        IReadOnlyList<string> configuredQueues,
        ILogger<RabbitMqProvider> logger,
        TimeProvider timeProvider)
    {
        // Interactive tool: fail fast and reconnect lazily on the next operation instead of
        // recovering in the background.
        _connectionFactory = new ConnectionFactory
        {
            Uri = settings.AmqpUri,
            ClientProvidedName = ClientName,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
        };
        _management = management;
        _configuredQueues = configuredQueues;
        _logger = logger;
        _timeProvider = timeProvider;
        _held = new HeldDeliveries(timeProvider, logger);
    }

    public ProviderKind Kind => ProviderKind.RabbitMQ;

    public ProviderCapabilities Capabilities => _capabilities;

    /// <summary>
    /// Probes the management API. On failure <see cref="ProviderCapabilities.ManagementUnavailableReason"/>
    /// explains why only config-listed queues are shown; the probe is retried on a later call once
    /// <see cref="ManagementReprobeInterval"/> has passed.
    /// </summary>
    public Task ProbeCapabilitiesAsync(CancellationToken cancellationToken) => EnsureManagementProbedAsync(cancellationToken);

    public async IAsyncEnumerable<MessagingEntity> ListEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.entities.list");

        await EnsureManagementProbedAsync(cancellationToken);

        IReadOnlyList<MessagingEntity> entities = _managementAvailable
            ? await DiscoverWithManagementAsync(cancellationToken)
            : await DiscoverConfiguredQueuesAsync(cancellationToken);

        foreach (MessagingEntity entity in entities)
        {
            yield return entity;
        }
    }

    public async Task<EntityRuntimeInfo> GetRuntimeInfoAsync(string entityPath, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.entities.runtime");

        (EntityKind kind, string name) = RabbitMqEntityPath.Parse(entityPath);
        if (kind == EntityKind.Exchange)
        {
            return new EntityRuntimeInfo();
        }

        await EnsureManagementProbedAsync(cancellationToken);

        try
        {
            if (!_managementAvailable)
            {
                return await CountReadyMessagesAsync(name, cancellationToken);
            }

            RabbitMqQueueInfo queue = await _management.GetQueueAsync(name, cancellationToken);
            IReadOnlyDictionary<string, uint> ready = await CountReadyMessagesAsync([queue], cancellationToken);
            return ToRuntimeInfo(queue, ready);
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            throw RabbitMqExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task<IReadOnlyList<ToolboxMessage>> PeekAsync(PeekRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.messages.peek");

        string queue = RequireActiveQueue(request.EntityPath, request.SubQueue);
        int maxMessages = Math.Clamp(request.MaxMessages, 1, MaxMessagesPerCall);

        return await OnQueueAsync(queue, request.EntityPath, async () => await IsStreamAsync(queue, cancellationToken)
            ? await PeekStreamAsync(queue, maxMessages, request.FromSequenceNumber, cancellationToken)
            : await PeekByRequeueAsync(queue, maxMessages, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ToolboxMessage>> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.messages.receive");

        string queue = RequireActiveQueue(request.EntityPath, request.SubQueue);
        int maxMessages = Math.Clamp(request.MaxMessages, 1, MaxMessagesPerCall);

        return await OnQueueAsync(queue, request.EntityPath, async () =>
        {
            await RequireNonStreamAsync(queue, "received and deleted", cancellationToken);

            await using IChannel channel = await CreateChannelAsync(null, cancellationToken);
            List<ToolboxMessage> received = [];
            while (received.Count < maxMessages
                && await channel.BasicGetAsync(queue, autoAck: true, cancellationToken) is { } result)
            {
                received.Add(ToToolboxMessage(result));
            }

            return (IReadOnlyList<ToolboxMessage>)received;
        }, cancellationToken);
    }

    public async Task SendAsync(string entityPath, IReadOnlyList<ToolboxMessage> messages, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.messages.send");

        if (messages.Any(message => message.ScheduledEnqueueTime is not null || message.SessionId is not null))
        {
            throw new NotSupportedByProviderException("RabbitMQ has no scheduled messages or sessions; clear Scheduled enqueue and Session ID.");
        }

        (EntityKind kind, string name) = RabbitMqEntityPath.Parse(entityPath);

        try
        {
            await using IChannel channel = await CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            foreach (ToolboxMessage message in messages)
            {
                // A queue is addressed through the default exchange with its name as routing key;
                // mandatory turns "nothing bound" into an error instead of a silent drop.
                (string exchange, string routingKey) = kind == EntityKind.Exchange
                    ? (name, message.RoutingKey ?? string.Empty)
                    : (string.Empty, name);

                await channel.BasicPublishAsync(
                    exchange,
                    routingKey,
                    mandatory: true,
                    RabbitMqMessageMapper.ToBasicProperties(message, _timeProvider.GetUtcNow()),
                    message.Body,
                    cancellationToken);
            }
        }
        catch (PublishException exception) when (exception.IsReturn && kind == EntityKind.Queue)
        {
            throw new MessagingEntityNotFoundException(entityPath, exception);
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            throw RabbitMqExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task<long> PurgeAsync(string entityPath, MessageSubQueue subQueue, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.entities.purge");

        string queue = RequireActiveQueue(entityPath, subQueue);

        return await OnQueueAsync(queue, entityPath, async () =>
        {
            await RequireNonStreamAsync(queue, "purged", cancellationToken);

            // Purge removes ready messages only; messages a consumer holds unacknowledged stay.
            await using IChannel channel = await CreateChannelAsync(null, cancellationToken);
            return (long)await channel.QueuePurgeAsync(queue, cancellationToken);
        }, cancellationToken);
    }

    // Held deliveries don't expire, so lock renewal has nothing to do; RabbitMQ has no sessions.
    public async Task<IReadOnlyList<ToolboxMessage>> ReceiveForSettlementAsync(
        string entityPath,
        MessageSubQueue subQueue,
        int maxMessages,
        TimeSpan? maxWaitTime,
        CancellationToken cancellationToken,
        TimeSpan? lockRenewalDuration = null,
        string? sessionId = null)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.messages.receive.settleable");

        string queue = RequireActiveQueue(entityPath, subQueue);
        int max = Math.Clamp(maxMessages, 1, MaxMessagesPerCall);

        return await OnQueueAsync(queue, entityPath, async () =>
        {
            await RequireNonStreamAsync(queue, "settled", cancellationToken);

            IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await _held.ReceiveAsync(
                queue, token => CreateChannelAsync(null, token), max, cancellationToken);

            return (IReadOnlyList<ToolboxMessage>)held
                .Select(entry => ToToolboxMessage(entry.Delivery) with { LockToken = entry.LockToken })
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ToolboxMessage>> ReceiveDeferredForSettlementAsync(ReceiveDeferredRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedByProviderException("RabbitMQ has no deferred messages.");

    // Held deliveries never expire: a still-held one reports no deadline (MaxValue).
    public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetHeldLockStatesAsync(
        string entityPath, MessageSubQueue subQueue, IReadOnlyList<string> lockTokens, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(
            _held.StillHeld(lockTokens).ToDictionary(token => token, _ => DateTimeOffset.MaxValue, StringComparer.Ordinal));

    public async Task<IReadOnlyList<string>> SettleAsync(SettleRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("rabbitmq.messages.settle");

        string queue = RequireActiveQueue(request.EntityPath, request.SubQueue);

        // Dead-lettering is a reject; without a dead-letter exchange the broker discards the
        // message, so the provider refuses rather than delete it silently (ADR 19).
        if (request.Operation == SettlementOperation.DeadLetter && await DeadLetterExchangeOfAsync(queue, cancellationToken) is null)
        {
            throw new NotSupportedByProviderException(
                $"'{queue}' has no dead-letter exchange (x-dead-letter-exchange or a policy), so a reject would discard the message. Configure one first.");
        }

        try
        {
            return await _held.SettleAsync(request.LockTokens, request.Operation, cancellationToken);
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            throw RabbitMqExceptionTranslator.Translate(exception, request.EntityPath);
        }
    }

    public Task<IReadOnlyList<long>> SettleBySequenceNumbersAsync(SettleBySequenceRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedByProviderException("RabbitMQ has no sequence numbers; receive the messages for settlement instead.");

    public Task<IReadOnlyList<long>> CancelScheduledAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken)
        => throw new NotSupportedByProviderException("RabbitMQ has no scheduled messages.");

    public Task<MessagingEntity> CreateOrUpdateEntityAsync(EntityDefinition definition, CancellationToken cancellationToken)
        => throw NotYetSupported("Entity management");

    public Task DeleteEntityAsync(string entityPath, CancellationToken cancellationToken)
        => throw NotYetSupported("Entity management");

    public Task<EntityDefinition> GetEntityDefinitionAsync(string entityPath, CancellationToken cancellationToken)
        => throw NotYetSupported("Entity management");

    // Always a clone from the typed and application properties; broker properties (death history,
    // delivery counts) never travel along. Removing the original is a separate, held-delivery ack.
    public Task<ToolboxMessage> PrepareResubmitAsync(
        string sourceEntityPath,
        MessageSubQueue sourceSubQueue,
        ToolboxMessage original,
        ResubmitOverrides overrides,
        CancellationToken cancellationToken)
    {
        Dictionary<string, object?> properties = new(overrides.ApplicationProperties ?? original.ApplicationProperties, StringComparer.Ordinal)
        {
            [ReplayFromProperty] = original.MessageId ?? "unknown",
        };

        return Task.FromResult(new ToolboxMessage
        {
            Body = overrides.Body is null ? original.Body : System.Text.Encoding.UTF8.GetBytes(overrides.Body),
            MessageId = overrides.MessageId ?? original.MessageId ?? Guid.NewGuid().ToString("N"),
            CorrelationId = overrides.CorrelationId ?? original.CorrelationId,
            ContentType = overrides.ContentType ?? original.ContentType,
            Subject = overrides.Subject ?? original.Subject,
            ReplyTo = overrides.ReplyTo ?? original.ReplyTo,
            TimeToLive = overrides.TimeToLive ?? original.TimeToLive,
            RoutingKey = original.RoutingKey,
            ApplicationProperties = properties,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _disposalTokenSource.CancelAsync();

        // Before the connection closes: held deliveries are nacked, not dropped with the channel.
        await _held.DisposeAsync();

        if (_connection is { } connection)
        {
            try
            {
                await connection.CloseAsync();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Closing the RabbitMQ connection failed; disposing it anyway.");
            }

            await connection.DisposeAsync();
        }

        _management.Dispose();
        _connectionLock.Dispose();
        _disposalTokenSource.Dispose();
    }

    private static NotSupportedByProviderException NotYetSupported(string operation)
        => new($"{operation} is not available for RabbitMQ connections yet.");

    private static string RequireActiveQueue(string entityPath, MessageSubQueue subQueue)
    {
        (EntityKind kind, string name) = RabbitMqEntityPath.Parse(entityPath);
        if (kind != EntityKind.Queue)
        {
            throw new NotSupportedByProviderException("Exchanges route messages but hold none; select a bound queue instead.");
        }

        if (subQueue != MessageSubQueue.Active)
        {
            throw new NotSupportedByProviderException(
                "RabbitMQ has no sub-queues; dead-lettered messages are in the queue bound to the dead-letter exchange.");
        }

        return name;
    }

    private async Task<IReadOnlyList<MessagingEntity>> DiscoverWithManagementAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RabbitMqQueueInfo> queues;
        IReadOnlyList<RabbitMqExchangeInfo> exchanges;
        try
        {
            queues = await _management.ListQueuesAsync(cancellationToken);
            exchanges = await _management.ListExchangesAsync(cancellationToken);
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            throw RabbitMqExceptionTranslator.Translate(exception, "/");
        }

        IReadOnlyDictionary<string, uint> ready = await CountReadyMessagesAsync(queues, cancellationToken);

        List<MessagingEntity> entities = [];
        foreach (RabbitMqQueueInfo queue in queues)
        {
            _streamQueues[queue.Name] = queue.IsStream;
            _deadLetterExchanges[queue.Name] = queue.DeadLetterExchange;
            entities.Add(new MessagingEntity
            {
                Path = RabbitMqEntityPath.ForQueue(queue.Name),
                Name = queue.Name,
                Kind = EntityKind.Queue,
                TypeName = queue.Type,
                Status = queue.State is null or "running" ? MessagingEntityStatus.Active : MessagingEntityStatus.Unknown,
                PeekMode = queue.IsStream ? PeekMode.NonDestructive : PeekMode.Requeue,
                DeadLetterExchange = queue.DeadLetterExchange,
                Operations = queue.IsStream ? StreamOperations : EntityOperations.All,
                Properties = RabbitMqEntityProperties.ForQueue(queue),
                RuntimeInfo = ToRuntimeInfo(queue, ready),
            });
        }

        // The default exchange is how queues are addressed directly (send to the queue instead), and
        // the predeclared amq.* exchanges are broker plumbing; neither is worth a tree node.
        foreach (RabbitMqExchangeInfo exchange in exchanges.Where(exchange =>
            !exchange.Internal && exchange.Name.Length > 0 && !exchange.Name.StartsWith("amq.", StringComparison.Ordinal)))
        {
            entities.Add(new MessagingEntity
            {
                Path = RabbitMqEntityPath.ForExchange(exchange.Name),
                Name = exchange.Name,
                Kind = EntityKind.Exchange,
                TypeName = exchange.Type,
                Status = MessagingEntityStatus.Active,
                Properties = RabbitMqEntityProperties.ForExchange(exchange),
            });
        }

        return entities;
    }

    // Without the management API, AMQP can't list anything: show the queues the host configured
    // (Aspire, environment) and count them with a passive declare.
    private async Task<IReadOnlyList<MessagingEntity>> DiscoverConfiguredQueuesAsync(CancellationToken cancellationToken)
    {
        List<MessagingEntity> entities = [];
        foreach (string queue in _configuredQueues)
        {
            EntityRuntimeInfo? runtimeInfo = null;
            try
            {
                runtimeInfo = await CountReadyMessagesAsync(queue, cancellationToken);
            }
            catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
            {
                _logger.LogDebug(exception, "Counting configured RabbitMQ queue {Queue} failed.", queue);
            }

            entities.Add(new MessagingEntity
            {
                Path = RabbitMqEntityPath.ForQueue(queue),
                Name = queue,
                Kind = EntityKind.Queue,
                Status = runtimeInfo is null ? MessagingEntityStatus.Unknown : MessagingEntityStatus.Active,
                PeekMode = PeekMode.Requeue,
                RuntimeInfo = runtimeInfo,
            });
        }

        return entities;
    }

    private async Task<EntityRuntimeInfo> CountReadyMessagesAsync(string queue, CancellationToken cancellationToken)
    {
        await using IChannel channel = await CreateChannelAsync(null, cancellationToken);
        QueueDeclareOk declared = await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
        return new EntityRuntimeInfo { ActiveMessageCount = declared.MessageCount };
    }

    // The management API refreshes its statistics every few seconds, so a count read right after
    // a send would lag. Ready counts come from a passive declare instead (exact, immediate); one
    // channel serves every queue, and a queue deleted meanwhile — or an unreachable data plane —
    // just keeps the management count. Streams are skipped: their declare reports no length.
    private async Task<IReadOnlyDictionary<string, uint>> CountReadyMessagesAsync(
        IReadOnlyList<RabbitMqQueueInfo> queues, CancellationToken cancellationToken)
    {
        Dictionary<string, uint> ready = new(StringComparer.Ordinal);
        IChannel? channel = null;
        try
        {
            foreach (RabbitMqQueueInfo queue in queues.Where(queue => !queue.IsStream))
            {
                channel ??= await CreateChannelAsync(null, cancellationToken);
                try
                {
                    ready[queue.Name] = (await channel.QueueDeclarePassiveAsync(queue.Name, cancellationToken)).MessageCount;
                }
                catch (OperationInterruptedException exception)
                {
                    // A failed declare closes the channel; the next queue opens a new one.
                    _logger.LogDebug(exception, "Counting RabbitMQ queue {Queue} failed.", queue.Name);
                    await channel.DisposeAsync();
                    channel = null;
                }
            }
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            // AMQP unreachable while the management API answers: keep its (slightly stale) counts.
            _logger.LogDebug(exception, "Counting RabbitMQ queues over AMQP failed; using management counts.");
        }
        finally
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }

        return ready;
    }

    // Active counts ready plus unacknowledged messages, like an Azure Service Bus active count
    // includes locked ones.
    private EntityRuntimeInfo ToRuntimeInfo(RabbitMqQueueInfo queue, IReadOnlyDictionary<string, uint> ready) => new()
    {
        ActiveMessageCount = ready.TryGetValue(queue.Name, out uint readyNow)
            ? readyNow + (queue.MessagesUnacknowledged ?? 0)
            : queue.Messages ?? (queue.MessagesReady ?? 0) + (queue.MessagesUnacknowledged ?? 0),
        SizeInBytes = queue.MessageBytes ?? 0,
        UpdatedAt = _timeProvider.GetUtcNow(),
    };

    // basic.get without auto-ack holds each message on this channel, so the next get returns the
    // next one. All of them are nacked with requeue at the end — never rejected: a reject (or a
    // channel closing with unacked messages) counts as a failed delivery on quorum queues and
    // can push messages to the dead-letter exchange.
    private async Task<IReadOnlyList<ToolboxMessage>> PeekByRequeueAsync(string queue, int maxMessages, CancellationToken cancellationToken)
    {
        await using IChannel channel = await CreateChannelAsync(null, cancellationToken);
        List<ToolboxMessage> messages = [];
        ulong lastDeliveryTag = 0;
        try
        {
            while (messages.Count < maxMessages
                && await channel.BasicGetAsync(queue, autoAck: false, cancellationToken) is { } result)
            {
                lastDeliveryTag = result.DeliveryTag;
                messages.Add(ToToolboxMessage(result));
            }
        }
        finally
        {
            if (lastDeliveryTag > 0)
            {
                await RequeueAllAsync(channel, lastDeliveryTag);
            }
        }

        return messages;
    }

    private async Task RequeueAllAsync(IChannel channel, ulong lastDeliveryTag)
    {
        try
        {
            await channel.BasicNackAsync(lastDeliveryTag, multiple: true, requeue: true, CancellationToken.None);
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            // The channel is gone, and with it the held messages: the broker requeued them on close.
            _logger.LogWarning(exception, "Requeueing peeked RabbitMQ messages failed; the broker requeues them as the channel closes.");
        }
    }

    // Streams are append-only logs: a consumer reads from an offset without removing anything.
    // The prefetch caps deliveries at the requested count, so nothing needs acknowledging.
    private async Task<IReadOnlyList<ToolboxMessage>> PeekStreamAsync(
        string queue, int maxMessages, long? fromOffset, CancellationToken cancellationToken)
    {
        await using IChannel channel = await CreateChannelAsync(null, cancellationToken);
        await channel.BasicQosAsync(0, (ushort)Math.Min(maxMessages, ushort.MaxValue), false, cancellationToken);

        List<ToolboxMessage> messages = [];
        TaskCompletionSource filled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AsyncEventingBasicConsumer consumer = new(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            lock (messages)
            {
                if (messages.Count < maxMessages)
                {
                    messages.Add(RabbitMqMessageMapper.ToToolboxMessage(
                        delivery.BasicProperties, delivery.Body, delivery.Exchange, delivery.RoutingKey, delivery.Redelivered));
                }

                if (messages.Count >= maxMessages)
                {
                    filled.TrySetResult();
                }
            }

            return Task.CompletedTask;
        };

        string consumerTag = await channel.BasicConsumeAsync(
            queue,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: new Dictionary<string, object?> { [RabbitMqMessageMapper.StreamOffsetHeader] = fromOffset is { } offset ? offset : "first" },
            consumer,
            cancellationToken);

        int seen = -1;
        while (!filled.Task.IsCompleted)
        {
            await Task.WhenAny(filled.Task, Task.Delay(StreamIdleTimeout, _timeProvider, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();

            int count;
            lock (messages)
            {
                count = messages.Count;
            }

            if (count == seen)
            {
                break;
            }

            seen = count;
        }

        await channel.BasicCancelAsync(consumerTag, noWait: false, CancellationToken.None);

        lock (messages)
        {
            return [.. messages];
        }
    }

    private static ToolboxMessage ToToolboxMessage(BasicGetResult result)
        => RabbitMqMessageMapper.ToToolboxMessage(
            result.BasicProperties, result.Body, result.Exchange, result.RoutingKey, result.Redelivered);

    private async Task<T> OnQueueAsync<T>(string queue, string entityPath, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _queueGates.GetOrAdd(queue, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        catch (Exception exception) when (RabbitMqExceptionTranslator.CanTranslate(exception))
        {
            throw RabbitMqExceptionTranslator.Translate(exception, entityPath);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RequireNonStreamAsync(string queue, string operation, CancellationToken cancellationToken)
    {
        if (await IsStreamAsync(queue, cancellationToken))
        {
            throw new NotSupportedByProviderException($"'{queue}' is a stream — an append-only log whose messages can't be {operation}.");
        }
    }

    private async Task<string?> DeadLetterExchangeOfAsync(string queue, CancellationToken cancellationToken)
    {
        if (_deadLetterExchanges.TryGetValue(queue, out string? exchange))
        {
            return exchange;
        }

        // Without the management API the queue's arguments and policies can't be read.
        if (!_managementAvailable)
        {
            return null;
        }

        exchange = (await _management.GetQueueAsync(queue, cancellationToken)).DeadLetterExchange;
        _deadLetterExchanges[queue] = exchange;
        return exchange;
    }

    private async Task<bool> IsStreamAsync(string queue, CancellationToken cancellationToken)
    {
        if (_streamQueues.TryGetValue(queue, out bool isStream))
        {
            return isStream;
        }

        if (!_managementAvailable)
        {
            return false;
        }

        isStream = (await _management.GetQueueAsync(queue, cancellationToken)).IsStream;
        _streamQueues[queue] = isStream;
        return isStream;
    }

    private async Task<IChannel> CreateChannelAsync(CreateChannelOptions? options, CancellationToken cancellationToken)
    {
        IConnection connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(options, cancellationToken);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } current)
            {
                return current;
            }

            if (_connection is { } closed)
            {
                await closed.DisposeAsync();
            }

            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task EnsureManagementProbedAsync(CancellationToken cancellationToken)
    {
        Task probeTask;
        lock (_probeLock)
        {
            bool retryDue = _probeTask is { IsCompleted: true }
                && !_managementAvailable
                && _timeProvider.GetUtcNow() - _lastProbeCompletedAt >= ManagementReprobeInterval;

            if (_probeTask is null || retryDue)
            {
                // Not bound to the caller's token: the task is shared by every caller until it completes.
                _probeTask = ProbeManagementAsync(_disposalTokenSource.Token);
            }

            probeTask = _probeTask;
        }

        await probeTask.WaitAsync(cancellationToken);
    }

    private async Task ProbeManagementAsync(CancellationToken disposalToken)
    {
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(disposalToken);
            timeoutCts.CancelAfter(ManagementProbeTimeout);

            await _management.ProbeAsync(timeoutCts.Token);
            SetManagementAvailability(null);
        }
        catch (OperationCanceledException) when (disposalToken.IsCancellationRequested)
        {
            // Provider disposed mid-probe; nothing to record.
        }
        catch (OperationCanceledException)
        {
            SetManagementAvailability($"it didn't answer within {ManagementProbeTimeout.TotalSeconds:0} s — the broker may still be starting");
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "RabbitMQ management API probe failed.");
            SetManagementAvailability(RabbitMqExceptionTranslator.CanTranslate(exception)
                ? RabbitMqExceptionTranslator.Translate(exception, "/").Message
                : exception.Message);
        }
        finally
        {
            _lastProbeCompletedAt = _timeProvider.GetUtcNow();
        }
    }

    private void SetManagementAvailability(string? unavailableReason)
    {
        bool wasAvailable = _managementAvailable;
        _managementAvailable = unavailableReason is null;
        Interlocked.Exchange(ref _capabilities, RabbitMqCapabilities.Default with
        {
            ManagementUnavailableReason = unavailableReason is null
                ? null
                : $"The RabbitMQ management API is unavailable ({unavailableReason}). Only queues listed in configuration are shown; enable the management plugin for discovery and counts.",
        });

        if (wasAvailable != _managementAvailable || unavailableReason is not null)
        {
            _logger.LogInformation(
                "RabbitMQ management API {State}. {Reason}",
                _managementAvailable ? "available" : "unavailable, retrying on later use",
                unavailableReason);
        }
    }
}
