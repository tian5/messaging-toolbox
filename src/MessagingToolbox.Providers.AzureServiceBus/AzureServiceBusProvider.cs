using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using MessagingToolbox.Providers.AzureServiceBus.Errors;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// <see cref="IMessagingProvider"/> over Azure.Messaging.ServiceBus. One code path serves cloud
/// namespaces and the emulator; the mode comes from the connection profile. Browsing is
/// side-effect free (peek only); destructive receives use receive-and-delete; settlement is
/// surgical and lock-based: <see cref="ReceiveForSettlementAsync"/> holds peek-lock locks
/// (they expire after the entity lock duration unless background renewal was requested) so
/// <see cref="SettleAsync"/> can settle exactly the received messages.
/// <para>
/// Session-enabled entities (ADR 12): peek needs no session — a plain receiver browses every
/// session — so browsing and count sampling stay lock-free. Receives on the active sub-queue go
/// through session receivers instead: the named session, or each available session in turn.
/// Settle-later receives hold their sessions until the received messages are settled.
/// </para>
/// </summary>
public sealed class AzureServiceBusProvider : IMessagingProvider, IAsyncDisposable
{
    // The broker caps a single peek at 100 messages, so larger requests page through sequence numbers.
    private const int PeekPageSize = 100;

    // NFR-2: never pull full queues into memory — paging stays bounded.
    private const int MaxPeekMessages = 5000;

    private const int MaxPeekPages = 100;

    private const int PurgeBatchSize = 100;

    private const int MaxLockedMessages = 1000;

    private const int MaxOriginalMessages = 5000;

    private const string ReplayFromPropertyName = "toolbox-replayed-from";

    private static readonly TimeSpan PurgeBatchWaitTime = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FollowUpReceiveWaitTime = TimeSpan.FromSeconds(1);

    // Upper bound on the scan-and-settle sweep, guarding only against pathological runaway —
    // cycle detection terminates the scan, so correctness no longer depends on this cap.
    private const int ScanSettleMaxScanned = 10_000;

    private const int ScanSettleBatchSize = 100;

    // Bounded parallelism for emulator count sampling: an expired cache must not serialize
    // 2×N sub-queue walks, but the emulator throttles under load, so concurrency stays low.
    private const int MaxConcurrentSamples = 4;

    // An empty receive is ambiguous: the sub-queue may be drained, or the head messages may still
    // be locked (lock release after receiver dispose is lazy on the emulator, and the cached
    // settle-later receiver may hold them too). This many consecutive empty batches are tolerated
    // — each receive already waits ScanSettleBatchWaitTime — before the scan concludes absence.
    private const int ScanSettleMaxEmptyBatches = 3;

    private static readonly TimeSpan ScanSettleBatchWaitTime = TimeSpan.FromSeconds(2);

    // Bound on the sessions one receive or purge walks through, guarding only against runaway.
    private const int MaxSessionsPerOperation = 1000;

    // Accept-next blocks until a session with available messages shows up, so "no session left"
    // is a timeout: the cloud reports ServiceTimeout, the emulator just waits until cancelled.
    private static readonly TimeSpan SessionAcceptWaitTime = TimeSpan.FromSeconds(2);

    private readonly ServiceBusClient _client;
    private readonly IAzureServiceBusManagement? _management;
    private readonly IReadOnlyList<IEmulatorEntityDiscovery> _discoveryChain;
    private readonly ILogger<AzureServiceBusProvider> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly bool _isEmulator;
    private IEmulatorRuntimeCountSampler? _runtimeCountSampler;

    // Settlement state: one receiver per (entity, sub-queue) plus the messages it holds.
    // Locks are renewed only when the receive asked for it (see the lock renewal loop below);
    // entries self-invalidate when the lock expires.
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusReceiver>> _peekLockReceivers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _receiverLocks = new();
    private readonly ConcurrentDictionary<string, LockedMessage> _lockedMessages = new();
    private readonly ConcurrentQueue<string> _lockTokenInsertionOrder = new();

    // Settle-later receives on session entities, keyed by entityPath|sessionId: a session receiver
    // stays open while it holds received messages, because their settlement must go through the
    // receiver that holds the session lock. Closing it releases the session to other receivers.
    private readonly ConcurrentDictionary<string, HeldSession> _heldSessions = new();

    // Entities known to require sessions — from management listings, or learned when the broker
    // rejects a non-session receive or a session-less send. Only the active sub-queue is
    // session-bound; dead-letter sub-queues are not.
    private readonly ConcurrentDictionary<string, byte> _sessionEntities = new();

    // Original SDK messages keyed by entityPath|subQueue|sequenceNumber for faithful clone/resubmit.
    private readonly ConcurrentDictionary<string, ServiceBusReceivedMessage> _originalMessages = new();
    private readonly ConcurrentQueue<string> _originalMessageInsertionOrder = new();

    // Background lock renewal for settle-later receives: the SDK only auto-renews on processors
    // (ServiceBusProcessorOptions.MaxAutoLockRenewalDuration), not on plain receivers, so renewal
    // is done here via RenewMessageLockAsync until each message's RenewUntil deadline.
    private static readonly TimeSpan LockRenewalInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan LockRenewalLeadTime = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ExpiredLockGracePeriod = TimeSpan.FromMinutes(2);

    private readonly CancellationTokenSource _disposalTokenSource = new();
    private readonly Lock _lockRenewalStartLock = new();
    private PeriodicTimer? _lockRenewalTimer;
    private Task? _lockRenewalLoop;
    private int _disposed;

    private readonly Lock _samplerLock = new();

    // Emulator admin probe: success is final, a failure is retried after the interval (on the next
    // tree load or management call), so management appears once a slow-starting emulator is up.
    internal static readonly TimeSpan ManagementReprobeInterval = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ManagementProbeTimeout = TimeSpan.FromSeconds(5);

    private ProviderCapabilities _capabilities;
    private Task? _capabilityProbeTask;
    private DateTimeOffset _lastProbeCompletedAt;
    private readonly Lock _capabilityProbeLock = new();

    public AzureServiceBusProvider(
        string connectionString,
        IReadOnlyList<IEmulatorEntityDiscovery> discoveryChain,
        ILogger<AzureServiceBusProvider> logger,
        TimeProvider timeProvider,
        IAzureServiceBusManagement management,
        IEmulatorRuntimeCountSampler? runtimeCountSampler = null)
    {
        _isEmulator = EmulatorDetection.IsEmulatorConnectionString(connectionString);
        // Interactive tool: fail fast and surface throttling instead of retrying for minutes
        // while continuing to pound an already-throttled broker.
        _client = new ServiceBusClient(connectionString, new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions
            {
                MaxRetries = 2,
                Delay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(3),
                TryTimeout = TimeSpan.FromSeconds(15),
                Mode = ServiceBusRetryMode.Exponential,
            },
        });
        ReceiverFactory = CreateReceiver;
        SessionReceiverFactory = AcceptSessionReceiverAsync;
        SenderFactory = _client.CreateSender;
        _discoveryChain = discoveryChain;
        _logger = logger;
        _timeProvider = timeProvider;
        _runtimeCountSampler = runtimeCountSampler;
        _capabilities = _isEmulator ? AzureServiceBusCapabilities.Emulator : AzureServiceBusCapabilities.Cloud;
        _management = management;
    }

    public static async Task<AzureServiceBusProvider> CreateAsync(
        string connectionString,
        IReadOnlyList<IEmulatorEntityDiscovery> discoveryChain,
        ILogger<AzureServiceBusProvider> logger,
        TimeProvider timeProvider,
        IAzureServiceBusManagement management,
        CancellationToken cancellationToken)
    {
        AzureServiceBusProvider provider = new(connectionString, discoveryChain, logger, timeProvider, management);
        await provider.ProbeCapabilitiesAsync(cancellationToken);
        return provider;
    }

    public ProviderKind Kind => ProviderKind.AzureServiceBus;

    /// <summary>
    /// Receiver creation seam: unit tests substitute the SDK receiver so the scan-and-settle loop
    /// can run without a broker. Production keeps the <see cref="ServiceBusClient"/> default.
    /// <paramref name="prefetchCount"/> pre-locks messages for the hold-locks scan walk.
    /// </summary>
    internal Func<string, MessageSubQueue, ServiceBusReceiveMode, int, ServiceBusReceiver> ReceiverFactory { get; set; }

    /// <summary>
    /// Session receiver seam, for the same reason as <see cref="ReceiverFactory"/>: accepts the named
    /// session of an entity, or the next available one when the session id is null.
    /// </summary>
    internal Func<string, string?, ServiceBusSessionReceiverOptions, CancellationToken, Task<ServiceBusSessionReceiver>> SessionReceiverFactory { get; set; }

    /// <summary>Sender creation seam, for the same reason as <see cref="ReceiverFactory"/>.</summary>
    internal Func<string, ServiceBusSender> SenderFactory { get; set; }

    public ProviderCapabilities Capabilities => _capabilities;

    /// <summary>
    /// Probes the connection for dynamic capability upgrades. On the Azure Service Bus emulator this
    /// tries the admin endpoint and flips <see cref="ProviderCapabilities.CanManageEntities"/> to
    /// <see langword="true"/> when it responds. Success is final; a failure records
    /// <see cref="ProviderCapabilities.ManagementUnavailableReason"/> and is retried by a later call
    /// once <see cref="ManagementReprobeInterval"/> has passed — the emulator is often still starting
    /// when the toolbox comes up.
    /// </summary>
    public Task ProbeCapabilitiesAsync(CancellationToken cancellationToken) => EnsureCapabilitiesProbedAsync(cancellationToken);

    public async IAsyncEnumerable<MessagingEntity> ListEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.entities.list");

        await EnsureCapabilitiesProbedAsync(cancellationToken);

        IReadOnlyList<MessagingEntity> entities = _isEmulator
            ? await DiscoverEmulatorEntitiesAsync(cancellationToken)
            : LearnSessionRequirements(await _management!.ListEntitiesAsync(cancellationToken));

        foreach (MessagingEntity entity in entities)
        {
            // Discovery channels that can't tell (Aspire model) still show what receives learned.
            yield return entity.RequiresSession || !_sessionEntities.ContainsKey(entity.Path)
                ? entity
                : entity with { RequiresSession = true };
        }
    }

    public async Task<EntityRuntimeInfo> GetRuntimeInfoAsync(string entityPath, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.entities.runtime");

        if (_isEmulator)
        {
            try
            {
                return await GetRuntimeCountSampler().SampleAsync(entityPath, cancellationToken);
            }
            catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
            {
                throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
            }
        }

        (string path, string? subscription) = ServiceBusEntityPath.Split(entityPath);
        if (subscription is not null)
        {
            return await _management!.GetSubscriptionRuntimeInfoAsync(path, subscription, cancellationToken);
        }

        try
        {
            return await _management!.GetQueueRuntimeInfoAsync(path, cancellationToken);
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            // Not a queue — try the topic of the same name before giving up.
        }

        try
        {
            return await _management!.GetTopicRuntimeInfoAsync(path, cancellationToken);
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            throw new MessagingEntityNotFoundException(entityPath, exception);
        }
    }

    public async Task<IReadOnlyList<ToolboxMessage>> PeekAsync(PeekRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.peek");

        int maxMessages = Math.Clamp(request.MaxMessages, 1, MaxPeekMessages);

        // A plain receiver peeks across every session of a session-enabled entity — no session lock.
        ServiceBusReceiver receiver = ReceiverFactory(request.EntityPath, request.SubQueue, ServiceBusReceiveMode.PeekLock, 0);
        try
        {
            // The broker returns scheduled messages from regular peeks; the Active view must show
            // only consumable messages and the Scheduled view only future ones.
            DateTimeOffset now = _timeProvider.GetUtcNow();
            List<ServiceBusReceivedMessage> peeked = await PeekPagesAsync(
                receiver,
                request.FromSequenceNumber,
                maxMessages,
                message => MatchesSubQueueView(request.SubQueue, message, now)
                    && (request.SessionId is null || message.SessionId == request.SessionId),
                cancellationToken);

            List<ToolboxMessage> collected = new(peeked.Count);
            foreach (ServiceBusReceivedMessage message in peeked)
            {
                CacheOriginalMessage(request.EntityPath, request.SubQueue, message);
                collected.Add(AzureServiceBusMessageMapper.ToToolboxMessage(message));
            }

            return collected;
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, request.EntityPath);
        }
        finally
        {
            await receiver.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<ToolboxMessage>> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.receive");

        ThrowIfScheduled(request.SubQueue, "receive");

        try
        {
            IReadOnlyList<ToolboxMessage> received = await ReceiveAndDeleteAsync(request, cancellationToken);
            _runtimeCountSampler?.Invalidate(request.EntityPath);
            return received;
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, request.EntityPath);
        }
    }

    public async Task SendAsync(string entityPath, IReadOnlyList<ToolboxMessage> messages, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.send");

        foreach (ToolboxMessage message in messages)
        {
            if (message.Body.LongLength > Capabilities.MaxMessageSizeBytes)
            {
                throw new MessagingProviderException(
                    $"Message body ({message.Body.LongLength:N0} bytes) exceeds the maximum of {Capabilities.MaxMessageSizeBytes:N0} bytes for this connection.");
            }
        }

        // Sending targets queues and topics; a subscription path resolves to its topic.
        (string sendPath, _) = ServiceBusEntityPath.Split(entityPath);

        if (_sessionEntities.ContainsKey(sendPath) && messages.Any(message => string.IsNullOrEmpty(message.SessionId)))
        {
            throw ServiceBusExceptionTranslator.SessionIdRequired(sendPath);
        }

        ServiceBusSender sender = SenderFactory(sendPath);
        try
        {
            await SendInBatchesAsync(sender, messages, cancellationToken);
            // Invalidate what was actually sent to: a topic send fans out to every subscription.
            _runtimeCountSampler?.Invalidate(sendPath);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.IsSessionRequirement(exception))
        {
            MarkSessionEntity(sendPath);
            throw ServiceBusExceptionTranslator.SessionIdRequired(sendPath, exception);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
        finally
        {
            await sender.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<ToolboxMessage>> ReceiveForSettlementAsync(
        string entityPath,
        MessageSubQueue subQueue,
        int maxMessages,
        TimeSpan? maxWaitTime,
        CancellationToken cancellationToken,
        TimeSpan? lockRenewalDuration = null,
        string? sessionId = null)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.receivesettle");

        ThrowIfScheduled(subQueue, "receive for settlement");

        DateTimeOffset? renewUntil = lockRenewalDuration is { } duration
            ? _timeProvider.GetUtcNow() + duration
            : null;

        try
        {
            ReceiveRequest request = new(entityPath, subQueue, maxMessages, maxWaitTime, sessionId);
            IReadOnlyList<ToolboxMessage> received = await WithReceiverLockAsync(entityPath, subQueue, async () =>
            {
                if (!UsesSessions(request))
                {
                    try
                    {
                        return await ReceivePeekLockAsync(request, renewUntil, cancellationToken);
                    }
                    catch (Exception exception) when (ServiceBusExceptionTranslator.IsSessionRequirement(exception))
                    {
                        MarkSessionEntity(entityPath);
                        await DropPeekLockReceiverAsync(entityPath, subQueue);
                    }
                }

                return await ReceiveSessionPeekLockAsync(request, renewUntil, cancellationToken);
            }, cancellationToken);

            _runtimeCountSampler?.Invalidate(entityPath);
            return received;
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task<IReadOnlyList<ToolboxMessage>> ReceiveDeferredForSettlementAsync(ReceiveDeferredRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.receivedeferred");

        ThrowIfScheduled(request.SubQueue, "receive deferred");

        if (request.Messages.Count == 0)
        {
            return [];
        }

        DateTimeOffset? renewUntil = request.LockRenewalDuration is { } duration
            ? _timeProvider.GetUtcNow() + duration
            : null;

        try
        {
            IReadOnlyList<ToolboxMessage> received = await WithReceiverLockAsync(request.EntityPath, request.SubQueue, async () =>
            {
                if (!UsesSessions(new ReceiveRequest(request.EntityPath, request.SubQueue, request.Messages.Count, null)))
                {
                    try
                    {
                        return await ReceiveDeferredPeekLockAsync(request, renewUntil, cancellationToken);
                    }
                    catch (Exception exception) when (request.SubQueue == MessageSubQueue.Active && IsDeferredSessionRejection(exception))
                    {
                        MarkSessionEntity(request.EntityPath);
                    }
                }

                return await ReceiveDeferredSessionPeekLockAsync(request, renewUntil, cancellationToken);
            }, cancellationToken);

            if (renewUntil is not null && received.Count > 0)
            {
                StartLockRenewal();
            }

            _runtimeCountSampler?.Invalidate(request.EntityPath);
            return received;
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, request.EntityPath);
        }
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetHeldLockStatesAsync(
        string entityPath,
        MessageSubQueue subQueue,
        IReadOnlyList<string> lockTokens,
        CancellationToken cancellationToken)
    {
        await PruneExpiredLocksAsync();

        Dictionary<string, DateTimeOffset> states = [];
        foreach (string lockToken in lockTokens)
        {
            if (_lockedMessages.TryGetValue(lockToken, out LockedMessage? locked))
            {
                states[lockToken] = LockExpiry(locked);
            }
        }

        return states;
    }

    public async Task<IReadOnlyList<string>> SettleAsync(SettleRequest request, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.settle");

        List<string> settled = [];
        HashSet<string> settledEntities = [];
        HashSet<string> touchedSessions = [];

        foreach (string lockToken in request.LockTokens)
        {
            if (!_lockedMessages.TryGetValue(lockToken, out LockedMessage? locked))
            {
                // Expired or already settled — report as not settled so the caller can say so.
                _logger.LogDebug("Lock '{LockToken}' on {EntityPath} is no longer held; skipping.", lockToken, request.EntityPath);
                continue;
            }

            if (locked.SessionKey is { } sessionKey)
            {
                touchedSessions.Add(sessionKey);
            }

            try
            {
                await WithReceiverLockAsync(locked.EntityPath, locked.SubQueue, async () =>
                {
                    ServiceBusReceiver receiver = ResolveSettlementReceiver(locked)
                        ?? throw new ServiceBusException("The session holding this message was released.", ServiceBusFailureReason.SessionLockLost);
                    await SettleOneAsync(receiver, locked.Message, request.Operation, request.Reason, request.Description, cancellationToken);
                }, cancellationToken);

                _lockedMessages.TryRemove(lockToken, out _);
                RemoveOriginalMessage(locked.EntityPath, locked.SubQueue, locked.Message.SequenceNumber);
                settled.Add(lockToken);

                // Settling moves counts on the message's own entity, which may differ from the
                // request path — invalidate each touched entity once.
                if (settledEntities.Add(locked.EntityPath))
                {
                    _runtimeCountSampler?.Invalidate(locked.EntityPath);
                }
            }
            catch (ServiceBusException exception)
            {
                // A lost lock invalidates the cached entry regardless of the translation outcome.
                if (exception.Reason is ServiceBusFailureReason.MessageLockLost or ServiceBusFailureReason.SessionLockLost)
                {
                    _lockedMessages.TryRemove(lockToken, out _);
                    RemoveOriginalMessage(locked.EntityPath, locked.SubQueue, locked.Message.SequenceNumber);

                    // A lost session lock takes every message received through that session with it.
                    if (exception.Reason == ServiceBusFailureReason.SessionLockLost && locked.SessionKey is { } lostSession)
                    {
                        await ReleaseSessionAsync(lostSession);
                    }

                    _logger.LogDebug(
                        exception,
                        "Lock '{LockToken}' on {EntityPath} was lost during {Operation}; skipping.",
                        lockToken,
                        request.EntityPath,
                        request.Operation);
                    continue;
                }

                throw ServiceBusExceptionTranslator.Translate(exception, request.EntityPath);
            }
        }

        // A session whose received messages are all settled is released at once, so the
        // application under test can pick it up again.
        foreach (string sessionKey in touchedSessions)
        {
            await ReleaseSessionIfIdleAsync(sessionKey, cancellationToken);
        }

        return settled;
    }

    public async Task<IReadOnlyList<long>> SettleBySequenceNumbersAsync(
        SettleBySequenceRequest request,
        CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.settlebysequence");

        if (request.SubQueue is not (MessageSubQueue.DeadLetter or MessageSubQueue.TransferDeadLetter))
        {
            throw new NotSupportedByProviderException(
                $"Cannot settle by sequence number on the {request.SubQueue} sub-queue — the bounded scan delivers non-matching messages too (+1 delivery count each), which is only safe on a dead-letter sub-queue where delivery count is terminal. Receive the messages for settlement (lock tokens) instead.");
        }

        HashSet<long> targets = [.. request.SequenceNumbers];
        List<long> settled = [];

        try
        {
            await WithReceiverLockAsync(request.EntityPath, request.SubQueue, async () =>
            {
                // Dedicated receiver: the scan owns it start to finish and disposes it, unlike the
                // cached peek-lock receivers used by lock-token settlement.
                //
                // The scan HOLDS the locks of everything it receives instead of abandoning:
                // abandoned messages return to the front of the retrieval order (confirmed against
                // the emulator), so with a 100-message batch on a deeper DLQ the same head batch
                // would cycle forever and tail targets would starve. Locked messages are skipped by
                // subsequent receives, so the walk reaches the tail in one pass. When the walk
                // ends, the held non-targets are abandoned one by one BEFORE the receiver is
                // disposed: receiver close does not release message locks reliably (lazy on the
                // emulator, lock-duration expiry on cloud), and a follow-up scan within that
                // window would starve on the still-locked head and report its targets as absent.
                // Duplicates that resurface because a lock expired before the walk reached them
                // are abandoned (their replacement lock is released, not held again).
                //
                // No PrefetchCount here: the emulator mishandles settlements on prefetched
                // messages (CompleteMessageAsync succeeds but the message stays — verified live),
                // and prefetch was only a walk-speed optimization.
                //
                // Cycle break: a batch of only already-seen sequences means the walk has wrapped
                // the whole sub-queue (held messages are skipped, so nothing unseen remains) —
                // any unsettled target is definitively absent. On brokers that do NOT skip locked
                // messages it also stops the head batch from spinning to the scan cap.
                //
                // Cost note: every held message is a delivery (+1 count — harmless on a DLQ, where
                // delivery count is terminal). There is no SDK seek-by-sequence for locks and no
                // batch settle, so bulk operations on lock-held messages should still prefer
                // SettleAsync (lock tokens). ScanSettleMaxScanned bounds only pathological cases.
                ServiceBusReceiver receiver = ReceiverFactory(request.EntityPath, request.SubQueue, ServiceBusReceiveMode.PeekLock, 0);
                List<ServiceBusReceivedMessage> heldMessages = [];
                try
                {
                    HashSet<long> seen = [];
                    bool FirstSight(long sequenceNumber) => seen.Add(sequenceNumber);

                    int scanned = 0;
                    int emptyBatches = 0;
                    while (settled.Count < targets.Count && scanned < ScanSettleMaxScanned)
                    {
                        IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.ReceiveMessagesAsync(
                            Math.Min(ScanSettleBatchSize, ScanSettleMaxScanned - scanned),
                            ScanSettleBatchWaitTime,
                            cancellationToken);

                        if (batch.Count == 0)
                        {
                            // Either the sub-queue is drained or the head is still locked (lazy
                            // release after the previous scan's dispose, or a settle-later receive
                            // holding it). Tolerate a few empty batches so a follow-up scan right
                            // after a previous one does not give up on still-releasing locks.
                            if (++emptyBatches > ScanSettleMaxEmptyBatches)
                            {
                                break;
                            }

                            continue;
                        }

                        emptyBatches = 0;
                        scanned += batch.Count;

                        bool batchHadNew = false;
                        bool batchSettledTarget = false;
                        foreach (ServiceBusReceivedMessage message in batch)
                        {
                            bool isFirstSight = FirstSight(message.SequenceNumber);
                            batchHadNew |= isFirstSight;

                            if (targets.Contains(message.SequenceNumber))
                            {
                                // Settle on first sight; a re-delivered copy after lock expiry is
                                // dropped like any other duplicate.
                                if (!settled.Contains(message.SequenceNumber))
                                {
                                    await SettleOneAsync(receiver, message, request.Operation, request.Reason, request.Description, cancellationToken);
                                    settled.Add(message.SequenceNumber);
                                }

                                batchSettledTarget = true;
                            }
                            else if (!isFirstSight)
                            {
                                // Lock-expiry redelivery of an already-held message: release it.
                                await receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken);
                            }
                            else
                            {
                                // First-sight non-targets: keep the lock held so the walk advances
                                // past them; released explicitly when the walk ends.
                                heldMessages.Add(message);
                            }
                        }

                        // Wrapped the whole sub-queue with nothing settled in this batch: any
                        // unsettled target is absent (or the broker is re-serving locked messages —
                        // either way, further scanning cannot make progress).
                        if (!batchHadNew && !batchSettledTarget)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    // Release every held lock explicitly before closing: receiver dispose does not
                    // release message locks reliably, and the next scan must not inherit a locked
                    // head. Best-effort — a lost lock expires on its own and must not fail the scan.
                    foreach (ServiceBusReceivedMessage held in heldMessages)
                    {
                        try
                        {
                            await receiver.AbandonMessageAsync(held, cancellationToken: CancellationToken.None);
                        }
                        catch (ServiceBusException exception)
                        {
                            _logger.LogDebug(
                                exception,
                                "Releasing the scan-held lock on sequence {SequenceNumber} of {EntityPath} failed; the lock expires on its own.",
                                held.SequenceNumber,
                                request.EntityPath);
                        }
                    }

                    await receiver.DisposeAsync();
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, request.EntityPath);
        }

        _runtimeCountSampler?.Invalidate(request.EntityPath);
        return settled;
    }

    public async Task<long> PurgeAsync(string entityPath, MessageSubQueue subQueue, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.entity.purge");

        ThrowIfScheduled(subQueue, "purge");

        try
        {
            long deleted;
            if (UsesSessions(new ReceiveRequest(entityPath, subQueue, 1, null)))
            {
                deleted = await PurgeSessionsAsync(entityPath, cancellationToken);
            }
            else
            {
                try
                {
                    deleted = await DrainAsync(
                        ReceiverFactory(entityPath, subQueue, ServiceBusReceiveMode.ReceiveAndDelete, 0),
                        PurgeBatchWaitTime,
                        cancellationToken);
                }
                catch (Exception exception) when (ServiceBusExceptionTranslator.IsSessionRequirement(exception))
                {
                    MarkSessionEntity(entityPath);
                    deleted = await PurgeSessionsAsync(entityPath, cancellationToken);
                }
            }

            deleted += await PurgeDeferredAsync(entityPath, subQueue, cancellationToken);

            _runtimeCountSampler?.Invalidate(entityPath);
            return deleted;
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
    }

    public async Task<IReadOnlyList<long>> CancelScheduledAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.cancelscheduled");

        // Scheduling happens on the entity itself; a subscription path resolves to its topic.
        (string sendPath, _) = ServiceBusEntityPath.Split(entityPath);

        List<long> cancelled = [];

        ServiceBusSender sender = SenderFactory(sendPath);
        try
        {
            // One call per message: a sequence number that is already enqueued or cancelled must not
            // abort the rest of the batch.
            foreach (long sequenceNumber in sequenceNumbers)
            {
                try
                {
                    await sender.CancelScheduledMessageAsync(sequenceNumber, cancellationToken);
                    cancelled.Add(sequenceNumber);
                }
                catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessageNotFound)
                {
                    _logger.LogDebug("Scheduled message {SequenceNumber} on {EntityPath} was not found; skipping.", sequenceNumber, entityPath);
                }
            }

            _runtimeCountSampler?.Invalidate(sendPath);
            return cancelled;
        }
        catch (Exception exception) when (ServiceBusExceptionTranslator.CanTranslate(exception))
        {
            throw ServiceBusExceptionTranslator.Translate(exception, entityPath);
        }
        finally
        {
            await sender.DisposeAsync();
        }
    }

    public IReadOnlyList<EntityTypeDescriptor> GetEntityTypes() => AzureServiceBusEntityTypes.All;

    // Subscription rules (filters and actions, FR-6.3) are the Service Bus routing rules; not yet.
    public RoutingRuleType? GetRoutingRuleType() => null;

    public Task<IReadOnlyList<RoutingRule>> ListRoutingRulesAsync(string entityPath, CancellationToken cancellationToken)
        => throw RulesNotSupported();

    public Task<RoutingRule> CreateRoutingRuleAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyDictionary<string, object?> options,
        CancellationToken cancellationToken)
        => throw RulesNotSupported();

    public Task DeleteRoutingRuleAsync(RoutingRule rule, CancellationToken cancellationToken)
        => throw RulesNotSupported();

    private static NotSupportedByProviderException RulesNotSupported()
        => new("Subscription rules can't be managed from the toolbox yet.");

    public async Task<MessagingEntity> CreateOrUpdateEntityAsync(EntityDefinition definition, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesProbedAsync(cancellationToken);

        if (!_capabilities.CanManageEntities)
        {
            throw new NotSupportedByProviderException(
                "This connection does not support entity management.");
        }

        MessagingEntity entity = definition.Kind switch
        {
            EntityKind.Queue => await _management!.CreateOrUpdateQueueAsync(definition.Name, definition.Options, cancellationToken),
            EntityKind.Topic => await _management!.CreateOrUpdateTopicAsync(definition.Name, definition.Options, cancellationToken),
            EntityKind.Subscription when definition.ParentPath is not null =>
                await _management!.CreateOrUpdateSubscriptionAsync(definition.ParentPath, definition.Name, definition.Options, cancellationToken),
            _ => throw new NotSupportedByProviderException($"A {definition.Kind} named '{definition.Name}' can't be created on Azure Service Bus."),
        };

        LearnSessionRequirements([entity]);
        return entity;
    }

    public async Task DeleteEntityAsync(string entityPath, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesProbedAsync(cancellationToken);

        if (!_capabilities.CanManageEntities)
        {
            throw new NotSupportedByProviderException(
                "This connection does not support entity management.");
        }

        (string path, string? subscription) = ServiceBusEntityPath.Split(entityPath);
        _sessionEntities.TryRemove(entityPath, out _);

        if (subscription is not null)
        {
            await _management!.DeleteSubscriptionAsync(path, subscription, cancellationToken);
            return;
        }

        try
        {
            await _management!.DeleteQueueAsync(path, cancellationToken);
            return;
        }
        catch (MessagingEntityNotFoundException)
        {
            // Not a queue — try the topic of the same name before giving up.
        }

        await _management!.DeleteTopicAsync(path, cancellationToken);
    }

    public async Task<EntityDefinition> GetEntityDefinitionAsync(string entityPath, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesProbedAsync(cancellationToken);

        if (!_capabilities.CanManageEntities)
        {
            throw new NotSupportedByProviderException(
                "This connection does not support entity management.");
        }

        (string path, string? subscription) = ServiceBusEntityPath.Split(entityPath);

        if (subscription is not null)
        {
            return await _management!.GetSubscriptionDefinitionAsync(path, subscription, cancellationToken);
        }

        try
        {
            return await _management!.GetQueueDefinitionAsync(path, cancellationToken);
        }
        catch (MessagingEntityNotFoundException)
        {
            // Not a queue — try the topic of the same name before giving up.
        }

        return await _management!.GetTopicDefinitionAsync(path, cancellationToken);
    }

    public async Task<ToolboxMessage> PrepareResubmitAsync(
        string sourceEntityPath,
        MessageSubQueue sourceSubQueue,
        ToolboxMessage original,
        ResubmitOverrides overrides,
        CancellationToken cancellationToken)
    {
        using Activity? activity = Telemetry.Source.StartActivity("servicebus.messages.resubmit.prepare");

        ServiceBusMessage clone;
        long? sequenceNumber = original.SequenceNumber;
        string cacheKey = OriginalMessageCacheKey(sourceEntityPath, sourceSubQueue, sequenceNumber);

        if (sequenceNumber.HasValue && _originalMessages.TryGetValue(cacheKey, out ServiceBusReceivedMessage? originalSdkMessage))
        {
            clone = new ServiceBusMessage(originalSdkMessage);
        }
        else
        {
            clone = AzureServiceBusMessageMapper.ToServiceBusMessage(original);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ApplyOverrides(clone, overrides);

        if (string.IsNullOrEmpty(clone.MessageId))
        {
            clone.MessageId = Guid.NewGuid().ToString("N");
        }

        clone.ApplicationProperties[ReplayFromPropertyName] =
            original.MessageId ?? original.SequenceNumber?.ToString() ?? "unknown";

        return MapPreparedMessage(clone);
    }

    private static ToolboxMessage MapPreparedMessage(ServiceBusMessage message) => new()
    {
        MessageId = message.MessageId,
        CorrelationId = message.CorrelationId,
        SessionId = message.SessionId,
        ReplyToSessionId = message.ReplyToSessionId,
        Subject = message.Subject,
        To = message.To,
        ReplyTo = message.ReplyTo,
        PartitionKey = message.PartitionKey,
        ContentType = message.ContentType,
        TimeToLive = message.TimeToLive == TimeSpan.MaxValue ? null : message.TimeToLive,
        ScheduledEnqueueTime = message.ScheduledEnqueueTime == default ? null : message.ScheduledEnqueueTime,
        Body = message.Body.ToArray(),
        ApplicationProperties = new Dictionary<string, object?>(message.ApplicationProperties),
    };

    // Idempotent and never throws on the way down: a profile change evicts the provider while
    // circuits may still hold it, so in-flight calls must fail cleanly (cancelled / disposed client)
    // rather than leave the client, receivers or the renewal loop behind.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _disposalTokenSource.CancelAsync();

        Task? lockRenewalLoop;
        lock (_lockRenewalStartLock)
        {
            _lockRenewalTimer?.Dispose();
            lockRenewalLoop = _lockRenewalLoop;
        }

        if (lockRenewalLoop is not null)
        {
            try
            {
                await lockRenewalLoop;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "The lock renewal loop ended with an error during disposal.");
            }
        }

        foreach (Lazy<ServiceBusReceiver> receiver in _peekLockReceivers.Values)
        {
            if (receiver.IsValueCreated)
            {
                try
                {
                    await receiver.Value.DisposeAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Disposing a settle-later receiver failed.");
                }
            }
        }

        foreach (HeldSession session in _heldSessions.Values)
        {
            try
            {
                await session.Receiver.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Disposing a held session receiver failed.");
            }
        }

        _heldSessions.Clear();

        // The receiver semaphores are deliberately not disposed: an in-flight operation may still
        // Release() one, which would throw ObjectDisposedException. Without AvailableWaitHandle
        // they hold no unmanaged resources, so the GC reclaims them.
        _peekLockReceivers.Clear();
        _lockedMessages.Clear();
        _originalMessages.Clear();

        if (_runtimeCountSampler is IAsyncDisposable disposableSampler)
        {
            await disposableSampler.DisposeAsync();
        }

        await _client.DisposeAsync();
        _disposalTokenSource.Dispose();
    }

    private async Task EnsureCapabilitiesProbedAsync(CancellationToken cancellationToken)
    {
        if (!_isEmulator || _management is null)
        {
            return;
        }

        Task probeTask;
        lock (_capabilityProbeLock)
        {
            bool retryDue = _capabilityProbeTask is { IsCompleted: true }
                && !_capabilities.CanManageEntities
                && _timeProvider.GetUtcNow() - _lastProbeCompletedAt >= ManagementReprobeInterval;

            if (_capabilityProbeTask is null || retryDue)
            {
                // Not bound to the caller's token: the task is shared by every caller until it completes.
                _capabilityProbeTask = ProbeCapabilitiesCoreAsync(_disposalTokenSource.Token);
            }

            probeTask = _capabilityProbeTask;
        }

        await probeTask.WaitAsync(cancellationToken);
    }

    private async Task ProbeCapabilitiesCoreAsync(CancellationToken disposalToken)
    {
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(disposalToken);
            timeoutCts.CancelAfter(ManagementProbeTimeout);

            await _management!.ProbeAsync(timeoutCts.Token);
            SetManagementAvailability(null);
        }
        catch (OperationCanceledException) when (disposalToken.IsCancellationRequested)
        {
            // Provider disposed mid-probe; nothing to record.
        }
        catch (OperationCanceledException)
        {
            SetManagementAvailability(
                $"The emulator admin endpoint didn't answer within {ManagementProbeTimeout.TotalSeconds:0} s — it may still be starting.");
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Azure Service Bus emulator admin endpoint probe failed.");
            SetManagementAvailability(exception.Message);
        }
        finally
        {
            _lastProbeCompletedAt = _timeProvider.GetUtcNow();
        }
    }

    private void SetManagementAvailability(string? unavailableReason)
    {
        ProviderCapabilities current = _capabilities;
        ProviderCapabilities updated = current with
        {
            CanManageEntities = unavailableReason is null,
            ManagementUnavailableReason = unavailableReason is null
                ? null
                : $"Entity management is unavailable: {unavailableReason} It needs the emulator admin endpoint (port 5300, emulator 2.0 or later); until it answers, the tree shows the entities modeled in the AppHost.",
        };
        Interlocked.Exchange(ref _capabilities, updated);

        if (current.CanManageEntities != updated.CanManageEntities || current.ManagementUnavailableReason != updated.ManagementUnavailableReason)
        {
            if (unavailableReason is null)
            {
                _logger.LogInformation("Azure Service Bus emulator admin endpoint is available; management enabled.");
            }
            else
            {
                _logger.LogInformation("Azure Service Bus emulator admin endpoint unavailable; management disabled, retrying on later use. {Reason}", unavailableReason);
            }
        }
    }

    // One SendMessagesAsync call is one broker batch, capped at the entity's max message size (256 KB
    // on the emulator) — many individually valid messages (e.g. "copies") would fail together with
    // MessageSizeExceeded. Fill size-checked batches and send each. A failure part-way leaves the
    // earlier batches sent, as with any multi-message send.
    private static async Task SendInBatchesAsync(ServiceBusSender sender, IReadOnlyList<ToolboxMessage> messages, CancellationToken cancellationToken)
    {
        Queue<ServiceBusMessage> pending = new(messages.Select(AzureServiceBusMessageMapper.ToServiceBusMessage));
        while (pending.Count > 0)
        {
            using ServiceBusMessageBatch batch = await sender.CreateMessageBatchAsync(cancellationToken);
            while (pending.Count > 0 && batch.TryAddMessage(pending.Peek()))
            {
                pending.Dequeue();
            }

            if (batch.Count == 0)
            {
                throw new MessagingProviderException(
                    $"A message (body plus properties) exceeds the broker's maximum batch size of {batch.MaxSizeInBytes:N0} bytes.");
            }

            await sender.SendMessagesAsync(batch, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ToolboxMessage>> ReceiveAndDeleteAsync(ReceiveRequest request, CancellationToken cancellationToken)
    {
        List<ServiceBusReceivedMessage> messages;
        if (UsesSessions(request))
        {
            messages = await ReceiveFromSessionsAsync(request, ServiceBusReceiveMode.ReceiveAndDelete, keepReceiver: null, cancellationToken);
        }
        else
        {
            try
            {
                ServiceBusReceiver receiver = ReceiverFactory(request.EntityPath, request.SubQueue, ServiceBusReceiveMode.ReceiveAndDelete, 0);
                await using (receiver)
                {
                    messages = await ReceiveInBatchesAsync(receiver, request, cancellationToken);
                }
            }
            catch (Exception exception) when (ServiceBusExceptionTranslator.IsSessionRequirement(exception))
            {
                MarkSessionEntity(request.EntityPath);
                messages = await ReceiveFromSessionsAsync(request, ServiceBusReceiveMode.ReceiveAndDelete, keepReceiver: null, cancellationToken);
            }
        }

        return messages.Select(AzureServiceBusMessageMapper.ToToolboxMessage).ToList();
    }

    private async Task<IReadOnlyList<ToolboxMessage>> ReceivePeekLockAsync(ReceiveRequest request, DateTimeOffset? renewUntil, CancellationToken cancellationToken)
    {
        ServiceBusReceiver receiver = GetPeekLockReceiver(request.EntityPath, request.SubQueue);
        List<ServiceBusReceivedMessage> messages = await ReceiveInBatchesAsync(receiver, request, cancellationToken);

        foreach (ServiceBusReceivedMessage message in messages)
        {
            TrackLockedMessage(request.EntityPath, request.SubQueue, message, renewUntil, sessionKey: null);
            CacheOriginalMessage(request.EntityPath, request.SubQueue, message);
        }

        if (renewUntil is not null && messages.Count > 0)
        {
            StartLockRenewal();
        }

        return messages.Select(AzureServiceBusMessageMapper.ToToolboxMessage).ToList();
    }

    // Runs under the entity's receiver lock, like every settle-later receive and settlement.
    private async Task<IReadOnlyList<ToolboxMessage>> ReceiveSessionPeekLockAsync(ReceiveRequest request, DateTimeOffset? renewUntil, CancellationToken cancellationToken)
    {
        List<ToolboxMessage> received = [];

        void Track(ServiceBusSessionReceiver receiver, string sessionKey, IReadOnlyList<ServiceBusReceivedMessage> messages)
        {
            foreach (ServiceBusReceivedMessage message in messages)
            {
                TrackLockedMessage(request.EntityPath, request.SubQueue, message, renewUntil, sessionKey);
                CacheOriginalMessage(request.EntityPath, request.SubQueue, message);
                // Session messages carry no lock of their own — the session lock is what expires.
                received.Add(AzureServiceBusMessageMapper.ToToolboxMessage(message) with { LockedUntil = receiver.SessionLockedUntil });
            }
        }

        if (request.SessionId is not null && _heldSessions.TryGetValue(SessionKey(request.EntityPath, request.SessionId), out HeldSession? held))
        {
            // Locked by an earlier settle-later receive: accepting it again would fail with
            // SessionCannotBeLocked, so keep receiving through the receiver that holds it.
            List<ServiceBusReceivedMessage> more = await ReceiveInBatchesAsync(held.Receiver, request, cancellationToken);
            Track(held.Receiver, held.Key, more);
            ExtendSessionRenewal(held, renewUntil);
        }
        else
        {
            await ReceiveFromSessionsAsync(request, ServiceBusReceiveMode.PeekLock, (receiver, messages) =>
            {
                if (messages.Count == 0)
                {
                    return false;
                }

                // Messages are tracked before the session is registered: pruning releases only
                // registered sessions, so it can never see this one without its messages.
                string sessionKey = SessionKey(request.EntityPath, receiver.SessionId);
                Track(receiver, sessionKey, messages);
                _heldSessions[sessionKey] = new HeldSession(sessionKey, request.EntityPath, receiver, renewUntil);
                return true;
            }, cancellationToken);
        }

        if (renewUntil is not null && received.Count > 0)
        {
            StartLockRenewal();
        }

        return received;
    }

    // Receives up to the request's budget from the named session, or from each available session
    // in turn. keepReceiver decides whether a session stays locked (settle-later holds it);
    // receivers it doesn't keep are closed right away, which releases the session.
    private async Task<List<ServiceBusReceivedMessage>> ReceiveFromSessionsAsync(
        ReceiveRequest request,
        ServiceBusReceiveMode receiveMode,
        Func<ServiceBusSessionReceiver, IReadOnlyList<ServiceBusReceivedMessage>, bool>? keepReceiver,
        CancellationToken cancellationToken)
    {
        int maxMessages = Math.Clamp(request.MaxMessages, 1, MaxPeekMessages);
        ServiceBusSessionReceiverOptions options = new() { ReceiveMode = receiveMode };
        List<ServiceBusReceivedMessage> received = [];

        async Task ReceiveFromAsync(ServiceBusSessionReceiver receiver, TimeSpan? maxWaitTime)
        {
            bool kept = false;
            try
            {
                List<ServiceBusReceivedMessage> batch = await ReceiveInBatchesAsync(
                    receiver,
                    request with { MaxMessages = maxMessages - received.Count, MaxWaitTime = maxWaitTime },
                    cancellationToken);
                received.AddRange(batch);
                kept = keepReceiver?.Invoke(receiver, batch) ?? false;
            }
            finally
            {
                if (!kept)
                {
                    await receiver.DisposeAsync();
                }
            }
        }

        if (request.SessionId is not null)
        {
            // A named session may be empty yet — wait the full time for its messages to arrive.
            ServiceBusSessionReceiver receiver = await SessionReceiverFactory(request.EntityPath, request.SessionId, options, cancellationToken);
            await ReceiveFromAsync(receiver, request.MaxWaitTime);
            return received;
        }

        // The first accept waits the full time for any session to have messages; later ones only
        // briefly. An accepted session has messages available, so its receive needs no long wait.
        TimeSpan acceptWaitTime = request.MaxWaitTime ?? SessionAcceptWaitTime;
        for (int sessions = 0; sessions < MaxSessionsPerOperation && received.Count < maxMessages; sessions++)
        {
            ServiceBusSessionReceiver? receiver = await TryAcceptNextSessionAsync(request.EntityPath, options, acceptWaitTime, cancellationToken);
            if (receiver is null)
            {
                break;
            }

            await ReceiveFromAsync(receiver, FollowUpReceiveWaitTime);
            acceptWaitTime = FollowUpReceiveWaitTime;
        }

        return received;
    }

    private async Task<ServiceBusSessionReceiver?> TryAcceptNextSessionAsync(
        string entityPath,
        ServiceBusSessionReceiverOptions options,
        TimeSpan waitTime,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(waitTime, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await SessionReceiverFactory(entityPath, null, options, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.ServiceTimeout)
        {
            return null;
        }
    }

    // Drains every available session with receive-and-delete; sessions another receiver holds are
    // skipped (accept-next never hands them out).
    private async Task<long> PurgeSessionsAsync(string entityPath, CancellationToken cancellationToken)
    {
        ServiceBusSessionReceiverOptions options = new() { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete };
        long deleted = 0;
        for (int sessions = 0; sessions < MaxSessionsPerOperation; sessions++)
        {
            ServiceBusSessionReceiver? receiver = await TryAcceptNextSessionAsync(entityPath, options, SessionAcceptWaitTime, cancellationToken);
            if (receiver is null)
            {
                break;
            }

            deleted += await DrainAsync(receiver, FollowUpReceiveWaitTime, cancellationToken);
        }

        return deleted;
    }

    // Receive-and-delete until an empty batch, then close the receiver.
    private static async Task<long> DrainAsync(ServiceBusReceiver receiver, TimeSpan batchWaitTime, CancellationToken cancellationToken)
    {
        await using (receiver)
        {
            long deleted = 0;
            while (true)
            {
                IReadOnlyList<ServiceBusReceivedMessage> batch =
                    await receiver.ReceiveMessagesAsync(PurgeBatchSize, batchWaitTime, cancellationToken);

                if (batch.Count == 0)
                {
                    return deleted;
                }

                deleted += batch.Count;
            }
        }
    }

    // Runs under the entity's receiver lock, like every settle-later receive and settlement.
    private async Task<IReadOnlyList<ToolboxMessage>> ReceiveDeferredPeekLockAsync(ReceiveDeferredRequest request, DateTimeOffset? renewUntil, CancellationToken cancellationToken)
    {
        ServiceBusReceiver receiver = GetPeekLockReceiver(request.EntityPath, request.SubQueue);
        List<ServiceBusReceivedMessage> messages = await ReceiveDeferredBySequenceAsync(
            receiver,
            request.Messages.Select(message => message.SequenceNumber).Distinct().ToList(),
            cancellationToken);

        foreach (ServiceBusReceivedMessage message in messages)
        {
            TrackLockedMessage(request.EntityPath, request.SubQueue, message, renewUntil, sessionKey: null);
            CacheOriginalMessage(request.EntityPath, request.SubQueue, message);
        }

        return messages.Select(AzureServiceBusMessageMapper.ToToolboxMessage).ToList();
    }

    // Deferred session messages are received through their own session: the named session is
    // accepted (or the one already held is reused) and stays held until its messages are settled.
    // A session locked by another receiver is skipped, like a message that can't be locked.
    private async Task<IReadOnlyList<ToolboxMessage>> ReceiveDeferredSessionPeekLockAsync(ReceiveDeferredRequest request, DateTimeOffset? renewUntil, CancellationToken cancellationToken)
    {
        List<ToolboxMessage> received = [];
        foreach (IGrouping<string?, DeferredMessageReference> session in request.Messages.GroupBy(message => message.SessionId))
        {
            if (session.Key is not { } sessionId)
            {
                _logger.LogDebug("Deferred messages without a session id on {EntityPath} can't be received; skipping.", request.EntityPath);
                continue;
            }

            string sessionKey = SessionKey(request.EntityPath, sessionId);
            _heldSessions.TryGetValue(sessionKey, out HeldSession? held);
            ServiceBusSessionReceiver receiver;
            if (held is not null)
            {
                receiver = held.Receiver;
            }
            else
            {
                try
                {
                    receiver = await SessionReceiverFactory(
                        request.EntityPath,
                        sessionId,
                        new ServiceBusSessionReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock },
                        cancellationToken);
                }
                catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.SessionCannotBeLocked)
                {
                    _logger.LogDebug(exception, "Session {SessionKey} is locked by another receiver; skipping its deferred messages.", sessionKey);
                    continue;
                }
            }

            List<ServiceBusReceivedMessage> messages;
            try
            {
                messages = await ReceiveDeferredBySequenceAsync(
                    receiver,
                    session.Select(message => message.SequenceNumber).Distinct().ToList(),
                    cancellationToken);
            }
            catch when (held is null)
            {
                await receiver.DisposeAsync();
                throw;
            }

            if (messages.Count == 0)
            {
                if (held is null)
                {
                    await receiver.DisposeAsync();
                }

                continue;
            }

            foreach (ServiceBusReceivedMessage message in messages)
            {
                TrackLockedMessage(request.EntityPath, request.SubQueue, message, renewUntil, sessionKey);
                CacheOriginalMessage(request.EntityPath, request.SubQueue, message);
                received.Add(AzureServiceBusMessageMapper.ToToolboxMessage(message) with { LockedUntil = receiver.SessionLockedUntil });
            }

            if (held is null)
            {
                _heldSessions[sessionKey] = new HeldSession(sessionKey, request.EntityPath, receiver, renewUntil);
            }
            else
            {
                ExtendSessionRenewal(held, renewUntil);
            }
        }

        return received;
    }

    // The broker locks a deferred batch all-or-nothing: one sequence number that is not deferred,
    // is locked by another receive or no longer exists fails the whole call (MessageNotFound,
    // verified on the emulator). Such a chunk is retried one by one so the rest still arrive.
    private async Task<List<ServiceBusReceivedMessage>> ReceiveDeferredBySequenceAsync(
        ServiceBusReceiver receiver,
        IReadOnlyList<long> sequenceNumbers,
        CancellationToken cancellationToken)
    {
        List<ServiceBusReceivedMessage> received = [];
        foreach (long[] chunk in sequenceNumbers.Chunk(PeekPageSize))
        {
            try
            {
                received.AddRange(await receiver.ReceiveDeferredMessagesAsync(chunk, cancellationToken));
                continue;
            }
            catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessageNotFound && chunk.Length > 1)
            {
                _logger.LogDebug(exception, "Receiving {Count} deferred messages failed as a batch; retrying one by one.", chunk.Length);
            }
            catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessageNotFound)
            {
                _logger.LogDebug(exception, "Deferred message {SequenceNumber} is not available; skipping.", chunk[0]);
                continue;
            }

            foreach (long sequenceNumber in chunk)
            {
                try
                {
                    received.AddRange(await receiver.ReceiveDeferredMessagesAsync([sequenceNumber], cancellationToken));
                }
                catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessageNotFound)
                {
                    _logger.LogDebug(exception, "Deferred message {SequenceNumber} is not available; skipping.", sequenceNumber);
                }
            }
        }

        return received;
    }

    // A plain receiver can't receive deferred messages of a session entity. The broker's rejection
    // is the usual "requires sessions" error, but the emulator reports a lost session lock instead
    // (verified live) — a plain receiver holds no session, so that can only mean the same thing.
    private static bool IsDeferredSessionRejection(Exception exception)
        => ServiceBusExceptionTranslator.IsSessionRequirement(exception)
            || exception is ServiceBusException { Reason: ServiceBusFailureReason.SessionLockLost };

    // A drain never sees deferred messages. Find them with a lock-free peek and delete them by
    // sequence number; ones locked by another receive (or a session someone else holds) stay.
    private async Task<long> PurgeDeferredAsync(string entityPath, MessageSubQueue subQueue, CancellationToken cancellationToken)
    {
        bool sessions = UsesSessions(new ReceiveRequest(entityPath, subQueue, 1, null));
        long deleted = 0;
        while (true)
        {
            List<ServiceBusReceivedMessage> deferred;
            ServiceBusReceiver peeker = ReceiverFactory(entityPath, subQueue, ServiceBusReceiveMode.PeekLock, 0);
            await using (peeker)
            {
                deferred = await PeekPagesAsync(
                    peeker,
                    null,
                    MaxPeekMessages,
                    message => message.State == ServiceBusMessageState.Deferred,
                    cancellationToken);
            }

            if (deferred.Count == 0)
            {
                return deleted;
            }

            long pass = sessions
                ? await DeleteDeferredSessionMessagesAsync(entityPath, deferred, cancellationToken)
                : await DeleteDeferredMessagesAsync(
                    ReceiverFactory(entityPath, subQueue, ServiceBusReceiveMode.ReceiveAndDelete, 0),
                    deferred.Select(message => message.SequenceNumber).ToList(),
                    cancellationToken);

            deleted += pass;

            // Stop once a pass finds only messages it can't delete, or the peek saw them all.
            if (pass == 0 || deferred.Count < MaxPeekMessages)
            {
                return deleted;
            }
        }
    }

    private async Task<long> DeleteDeferredSessionMessagesAsync(string entityPath, List<ServiceBusReceivedMessage> deferred, CancellationToken cancellationToken)
    {
        ServiceBusSessionReceiverOptions options = new() { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete };
        long deleted = 0;
        foreach (IGrouping<string, ServiceBusReceivedMessage> session in deferred.Where(message => message.SessionId is not null).GroupBy(message => message.SessionId))
        {
            ServiceBusSessionReceiver receiver;
            try
            {
                receiver = await SessionReceiverFactory(entityPath, session.Key, options, cancellationToken);
            }
            catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.SessionCannotBeLocked)
            {
                _logger.LogDebug(exception, "Session {SessionId} on {EntityPath} is locked; its deferred messages stay.", session.Key, entityPath);
                continue;
            }

            deleted += await DeleteDeferredMessagesAsync(receiver, session.Select(message => message.SequenceNumber).ToList(), cancellationToken);
        }

        return deleted;
    }

    // Receive-and-delete by sequence number, then close the receiver.
    private async Task<long> DeleteDeferredMessagesAsync(ServiceBusReceiver receiver, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken)
    {
        await using (receiver)
        {
            List<ServiceBusReceivedMessage> deleted = await ReceiveDeferredBySequenceAsync(receiver, sequenceNumbers, cancellationToken);
            return deleted.Count;
        }
    }

    private void ExtendSessionRenewal(HeldSession held, DateTimeOffset? renewUntil)
    {
        if (renewUntil > held.RenewUntil || (renewUntil is not null && held.RenewUntil is null))
        {
            _heldSessions[held.Key] = held with { RenewUntil = renewUntil };
        }
    }

    private bool UsesSessions(ReceiveRequest request)
    {
        if (request.SubQueue != MessageSubQueue.Active)
        {
            if (request.SessionId is not null)
            {
                throw new NotSupportedByProviderException(
                    $"Sessions apply to the active sub-queue only — the {request.SubQueue} sub-queue is not session-bound. Receive without a session filter, or peek with one.");
            }

            return false;
        }

        return request.SessionId is not null || _sessionEntities.ContainsKey(request.EntityPath);
    }

    private void MarkSessionEntity(string entityPath)
    {
        if (_sessionEntities.TryAdd(entityPath, 0))
        {
            _logger.LogDebug("{EntityPath} requires sessions; receives switch to session receivers.", entityPath);
        }
    }

    // Management results are authoritative both ways; the Aspire model can't tell, so its
    // entities never clear what a receive learned.
    private IReadOnlyList<MessagingEntity> LearnSessionRequirements(IReadOnlyList<MessagingEntity> entities)
    {
        foreach (MessagingEntity entity in entities)
        {
            if (entity.RequiresSession)
            {
                _sessionEntities.TryAdd(entity.Path, 0);
            }
            else
            {
                _sessionEntities.TryRemove(entity.Path, out _);
            }
        }

        return entities;
    }

    private async Task DropPeekLockReceiverAsync(string entityPath, MessageSubQueue subQueue)
    {
        if (_peekLockReceivers.TryRemove(ReceiverKey(entityPath, subQueue), out Lazy<ServiceBusReceiver>? receiver) && receiver.IsValueCreated)
        {
            await receiver.Value.DisposeAsync();
        }
    }

    private ServiceBusReceiver? ResolveSettlementReceiver(LockedMessage locked)
    {
        if (locked.SessionKey is null)
        {
            return GetPeekLockReceiver(locked.EntityPath, locked.SubQueue);
        }

        return _heldSessions.TryGetValue(locked.SessionKey, out HeldSession? session) ? session.Receiver : null;
    }

    private DateTimeOffset LockExpiry(LockedMessage locked)
    {
        if (locked.SessionKey is null)
        {
            return locked.Message.LockedUntil;
        }

        return _heldSessions.TryGetValue(locked.SessionKey, out HeldSession? session)
            ? session.Receiver.SessionLockedUntil
            : DateTimeOffset.MinValue;
    }

    private async Task ReleaseSessionIfIdleAsync(string sessionKey, CancellationToken cancellationToken)
    {
        if (!_heldSessions.TryGetValue(sessionKey, out HeldSession? session))
        {
            return;
        }

        // Under the entity's receiver lock: a settle-later receive naming this session may be
        // adding messages to it right now.
        await WithReceiverLockAsync(session.EntityPath, MessageSubQueue.Active, async () =>
        {
            if (_lockedMessages.Values.Any(locked => locked.SessionKey == sessionKey))
            {
                return;
            }

            await ReleaseSessionAsync(sessionKey);
        }, cancellationToken);
    }

    // Forgets the session's messages too: once the session receiver closes, their settlement is
    // impossible and the broker hands the session (with whatever is left) to the next receiver.
    private async Task ReleaseSessionAsync(string sessionKey)
    {
        foreach (KeyValuePair<string, LockedMessage> entry in _lockedMessages)
        {
            if (entry.Value.SessionKey == sessionKey)
            {
                _lockedMessages.TryRemove(entry);
            }
        }

        if (_heldSessions.TryRemove(sessionKey, out HeldSession? session))
        {
            try
            {
                await session.Receiver.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Releasing session {SessionKey} failed; its lock expires on its own.", sessionKey);
            }
        }
    }

    private static string SessionKey(string entityPath, string sessionId) => $"{entityPath}|{sessionId}";

    // A single peek never returns more than a page, so keep paging from the highest sequence number
    // seen until the budget is filled or the sub-queue is exhausted. Sub-queues (dead-letter above
    // all) do not always hand back an ordered page, so the cursor follows the maximum and repeats
    // are skipped.
    private static async Task<List<ServiceBusReceivedMessage>> PeekPagesAsync(
        ServiceBusReceiver receiver,
        long? fromSequenceNumber,
        int maxMessages,
        Func<ServiceBusReceivedMessage, bool> include,
        CancellationToken cancellationToken)
    {
        List<ServiceBusReceivedMessage> collected = [];
        HashSet<long> seen = [];
        long? fromSequence = fromSequenceNumber;

        for (int page = 0; page < MaxPeekPages && collected.Count < maxMessages; page++)
        {
            IReadOnlyList<ServiceBusReceivedMessage> batch =
                await receiver.PeekMessagesAsync(PeekPageSize, fromSequence, cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            int fresh = 0;
            long maxSequence = long.MinValue;
            foreach (ServiceBusReceivedMessage message in batch)
            {
                maxSequence = Math.Max(maxSequence, message.SequenceNumber);
                if (!seen.Add(message.SequenceNumber))
                {
                    continue;
                }

                fresh++;
                if (collected.Count < maxMessages && include(message))
                {
                    collected.Add(message);
                }
            }

            // No new messages (or a cursor that stopped advancing) means the end of the sub-queue.
            if (fresh == 0 || (fromSequence is not null && maxSequence < fromSequence))
            {
                break;
            }

            fromSequence = maxSequence + 1;
        }

        return collected;
    }

    // The broker serves a receive call from one batch, so requests above the page size are filled
    // by repeated calls until the budget is reached or the sub-queue runs dry. Only the first call
    // waits the full MaxWaitTime (for messages to arrive at all); follow-ups use a short wait, so
    // draining a few messages doesn't block a whole extra wait period on the final empty batch.
    private static async Task<List<ServiceBusReceivedMessage>> ReceiveInBatchesAsync(
        ServiceBusReceiver receiver,
        ReceiveRequest request,
        CancellationToken cancellationToken)
    {
        int maxMessages = Math.Clamp(request.MaxMessages, 1, MaxPeekMessages);
        List<ServiceBusReceivedMessage> received = [];

        while (received.Count < maxMessages)
        {
            TimeSpan? maxWaitTime = received.Count == 0 || request.MaxWaitTime is null
                ? request.MaxWaitTime
                : TimeSpan.FromTicks(Math.Min(request.MaxWaitTime.Value.Ticks, FollowUpReceiveWaitTime.Ticks));

            IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.ReceiveMessagesAsync(
                Math.Min(PeekPageSize, maxMessages - received.Count),
                maxWaitTime,
                cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            received.AddRange(batch);
        }

        return received;
    }

    private static Task SettleOneAsync(
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage message,
        SettlementOperation operation,
        string? deadLetterReason,
        string? deadLetterDescription,
        CancellationToken cancellationToken)
        => operation switch
        {
            SettlementOperation.Complete => receiver.CompleteMessageAsync(message, cancellationToken),
            SettlementOperation.Abandon => receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken),
            SettlementOperation.Defer => receiver.DeferMessageAsync(message, cancellationToken: cancellationToken),
            SettlementOperation.DeadLetter => receiver.DeadLetterMessageAsync(
                message,
                deadLetterReason ?? "Unspecified",
                deadLetterDescription,
                cancellationToken),
            _ => throw new NotSupportedByProviderException($"Settlement operation '{operation}' is not supported."),
        };

    private void TrackLockedMessage(string entityPath, MessageSubQueue subQueue, ServiceBusReceivedMessage message, DateTimeOffset? renewUntil, string? sessionKey)
    {
        _lockedMessages[message.LockToken] = new LockedMessage(entityPath, subQueue, message, renewUntil, sessionKey);
        _lockTokenInsertionOrder.Enqueue(message.LockToken);

        while (_lockedMessages.Count > MaxLockedMessages && _lockTokenInsertionOrder.TryDequeue(out string? oldest))
        {
            if (_lockedMessages.TryRemove(oldest, out LockedMessage? removed))
            {
                RemoveOriginalMessage(removed.EntityPath, removed.SubQueue, removed.Message.SequenceNumber);
            }
        }
    }

    // A lock past its expiry can no longer be settled or renewed. Dropping the entry keeps renewal
    // from retrying it and GetHeldLockStatesAsync from reporting it as held. LockedUntil comes from
    // the broker's clock, which can drift from ours (Docker VM after sleep), so entries get a grace
    // period instead of being pruned the instant they look expired. The cached original stays: the
    // message is back in the queue and may still be resubmitted from a peek. A session whose lock
    // expired is closed together with its messages.
    private async Task PruneExpiredLocksAsync()
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - ExpiredLockGracePeriod;
        foreach (HeldSession session in _heldSessions.Values)
        {
            if (session.Receiver.SessionLockedUntil <= cutoff)
            {
                await ReleaseSessionAsync(session.Key);
            }
        }

        foreach (KeyValuePair<string, LockedMessage> entry in _lockedMessages)
        {
            if (LockExpiry(entry.Value) <= cutoff)
            {
                _lockedMessages.TryRemove(entry);
            }
        }
    }

    // Settle-later receives on different entities hold different receiver locks, so two can get
    // here at once; exactly one loop (and one timer) may ever start.
    private void StartLockRenewal()
    {
        lock (_lockRenewalStartLock)
        {
            if (_lockRenewalLoop is not null || Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            PeriodicTimer timer = new(LockRenewalInterval, _timeProvider);
            _lockRenewalTimer = timer;
            _lockRenewalLoop = Task.Run(() => RenewLocksLoopAsync(timer));
        }
    }

    // Renews every held lock whose deadline has not passed and whose expiry is within the lead
    // time. Per-message failures are logged and skipped — renewal is best-effort and must never
    // disturb settlement or browsing.
    private async Task RenewLocksLoopAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(_disposalTokenSource.Token))
            {
                await PruneExpiredLocksAsync();
                DateTimeOffset now = _timeProvider.GetUtcNow();
                await RenewSessionLocksAsync(now);
                foreach (LockedMessage locked in _lockedMessages.Values)
                {
                    // Session messages have no lock of their own; their session is renewed above.
                    if (locked.SessionKey is not null || locked.RenewUntil is not { } deadline || deadline <= now)
                    {
                        continue;
                    }

                    if (locked.Message.LockedUntil > now + LockRenewalLeadTime)
                    {
                        continue;
                    }

                    try
                    {
                        ServiceBusReceiver receiver = GetPeekLockReceiver(locked.EntityPath, locked.SubQueue);
                        await receiver.RenewMessageLockAsync(locked.Message, _disposalTokenSource.Token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogDebug(
                            exception,
                            "Lock renewal for sequence {SequenceNumber} on {EntityPath} failed; the lock expires at {LockedUntil}.",
                            locked.Message.SequenceNumber,
                            locked.EntityPath,
                            locked.Message.LockedUntil);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Provider disposed — the loop ends.
        }
    }

    private async Task RenewSessionLocksAsync(DateTimeOffset now)
    {
        foreach (HeldSession session in _heldSessions.Values)
        {
            if (session.RenewUntil is not { } deadline || deadline <= now || session.Receiver.SessionLockedUntil > now + LockRenewalLeadTime)
            {
                continue;
            }

            try
            {
                await session.Receiver.RenewSessionLockAsync(_disposalTokenSource.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(
                    exception,
                    "Session lock renewal for {SessionKey} failed; the lock expires at {LockedUntil}.",
                    session.Key,
                    session.Receiver.SessionLockedUntil);
            }
        }
    }

    private void CacheOriginalMessage(string entityPath, MessageSubQueue subQueue, ServiceBusReceivedMessage message)
    {
        string key = OriginalMessageCacheKey(entityPath, subQueue, message.SequenceNumber);
        _originalMessages[key] = message;
        _originalMessageInsertionOrder.Enqueue(key);

        while (_originalMessages.Count > MaxOriginalMessages && _originalMessageInsertionOrder.TryDequeue(out string? oldest))
        {
            _originalMessages.TryRemove(oldest, out _);
        }
    }

    internal void CacheOriginalForTest(string entityPath, MessageSubQueue subQueue, ServiceBusReceivedMessage message)
        => CacheOriginalMessage(entityPath, subQueue, message);

    private void RemoveOriginalMessage(string entityPath, MessageSubQueue subQueue, long? sequenceNumber)
    {
        if (!sequenceNumber.HasValue)
        {
            return;
        }

        _originalMessages.TryRemove(OriginalMessageCacheKey(entityPath, subQueue, sequenceNumber.Value), out _);
    }

    private static string OriginalMessageCacheKey(string entityPath, MessageSubQueue subQueue, long? sequenceNumber)
        => sequenceNumber.HasValue ? $"{entityPath}|{subQueue}|{sequenceNumber.Value}" : string.Empty;

    private ServiceBusReceiver GetPeekLockReceiver(string entityPath, MessageSubQueue subQueue)
    {
        string key = ReceiverKey(entityPath, subQueue);
        return _peekLockReceivers
            .GetOrAdd(key, _ => new Lazy<ServiceBusReceiver>(
                () => ReceiverFactory(entityPath, subQueue, ServiceBusReceiveMode.PeekLock, 0),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    private async Task<T> WithReceiverLockAsync<T>(string entityPath, MessageSubQueue subQueue, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = _receiverLocks.GetOrAdd(ReceiverKey(entityPath, subQueue), _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task WithReceiverLockAsync(string entityPath, MessageSubQueue subQueue, Func<Task> action, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = _receiverLocks.GetOrAdd(ReceiverKey(entityPath, subQueue), _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string ReceiverKey(string entityPath, MessageSubQueue subQueue) => $"{entityPath}|{subQueue}";

    private async Task<IReadOnlyList<MessagingEntity>> DiscoverEmulatorEntitiesAsync(CancellationToken cancellationToken)
    {
        if (_capabilities.CanManageEntities && _management is not null)
        {
            try
            {
                IReadOnlyList<MessagingEntity> listed = LearnSessionRequirements(await _management.ListEntitiesAsync(cancellationToken));
                return await EnrichWithSampledCountsAsync(listed, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Admin listing of emulator entities failed; falling back to discovery chain.");
            }
        }

        IReadOnlyList<MessagingEntity>? discovered = null;
        foreach (IEmulatorEntityDiscovery strategy in _discoveryChain)
        {
            discovered = await strategy.DiscoverAsync(cancellationToken);
            if (discovered is not null)
            {
                break;
            }
        }

        if (discovered is null)
        {
            _logger.LogWarning("No emulator discovery strategy yielded entities; the tree will be empty.");
            return [];
        }

        return await EnrichWithSampledCountsAsync(discovered, cancellationToken);
    }

    // The emulator admin runtime properties report 0, so counts are sampled over AMQP
    // (architecture doc §6: discovery results merged with AMQP-obtained runtime counts).
    private async Task<IReadOnlyList<MessagingEntity>> EnrichWithSampledCountsAsync(IReadOnlyList<MessagingEntity> entities, CancellationToken cancellationToken)
    {
        IEmulatorRuntimeCountSampler sampler = GetRuntimeCountSampler();
        MessagingEntity[] enriched = new MessagingEntity[entities.Count];
        using SemaphoreSlim gate = new(MaxConcurrentSamples, MaxConcurrentSamples);
        int sampledCount = 0;
        int unreachableCount = 0;
        MessagingProviderUnavailableException? unreachable = null;

        async Task SampleOneAsync(int index)
        {
            MessagingEntity entity = entities[index];

            // Topics hold no messages of their own — subscriptions carry the counts.
            if (entity.Kind is EntityKind.Topic)
            {
                enriched[index] = entity;
                return;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                Interlocked.Increment(ref sampledCount);
                (EntityRuntimeInfo? counts, MessagingProviderUnavailableException? failure) =
                    await TrySampleCountsAsync(sampler, entity.Path, cancellationToken);
                if (failure is not null)
                {
                    Interlocked.Increment(ref unreachableCount);
                    unreachable = failure;
                }

                enriched[index] = counts is null ? entity : entity with { RuntimeInfo = counts };
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, entities.Count).Select(SampleOneAsync));

        // One entity failing is that entity's problem; every entity failing to connect means the
        // broker itself is down or still starting — say so instead of showing a count-less tree.
        if (sampledCount > 0 && unreachableCount == sampledCount)
        {
            throw unreachable!;
        }

        return enriched;
    }

    private async Task<(EntityRuntimeInfo? Counts, MessagingProviderUnavailableException? Unreachable)> TrySampleCountsAsync(
        IEmulatorRuntimeCountSampler sampler, string entityPath, CancellationToken cancellationToken)
    {
        try
        {
            return (await sampler.SampleAsync(entityPath, cancellationToken), null);
        }
        catch (Exception exception)
        {
            // Counts are best-effort: a failing entity must never break the whole tree.
            _logger.LogDebug(exception, "Sampling runtime counts for {EntityPath} failed; counts unavailable for this entity.", entityPath);
            Exception translated = ServiceBusExceptionTranslator.CanTranslate(exception)
                ? ServiceBusExceptionTranslator.Translate(exception, entityPath)
                : exception;
            return (null, translated as MessagingProviderUnavailableException);
        }
    }

    // Locked so concurrent first calls share one sampler: a second instance would cache counts
    // that the provider's invalidations never reach, and would never be disposed.
    private IEmulatorRuntimeCountSampler GetRuntimeCountSampler()
    {
        if (Volatile.Read(ref _runtimeCountSampler) is { } sampler)
        {
            return sampler;
        }

        lock (_samplerLock)
        {
            return _runtimeCountSampler ??= new PeekRuntimeCountSampler(_client, _timeProvider);
        }
    }

    private Task<ServiceBusSessionReceiver> AcceptSessionReceiverAsync(
        string entityPath,
        string? sessionId,
        ServiceBusSessionReceiverOptions options,
        CancellationToken cancellationToken)
    {
        (string path, string? subscription) = ServiceBusEntityPath.Split(entityPath);
        return (subscription, sessionId) switch
        {
            (null, null) => _client.AcceptNextSessionAsync(path, options, cancellationToken),
            (null, not null) => _client.AcceptSessionAsync(path, sessionId, options, cancellationToken),
            (not null, null) => _client.AcceptNextSessionAsync(path, subscription, options, cancellationToken),
            (not null, not null) => _client.AcceptSessionAsync(path, subscription, sessionId, options, cancellationToken),
        };
    }

    private ServiceBusReceiver CreateReceiver(string entityPath, MessageSubQueue subQueue, ServiceBusReceiveMode receiveMode, int prefetchCount = 0)
    {
        ServiceBusReceiverOptions options = new()
        {
            ReceiveMode = receiveMode,
            SubQueue = MapSubQueue(subQueue),
            PrefetchCount = prefetchCount,
        };

        (string path, string? subscription) = ServiceBusEntityPath.Split(entityPath);
        return subscription is null
            ? _client.CreateReceiver(path, options)
            : _client.CreateReceiver(path, subscription, options);
    }

    private static bool MatchesSubQueueView(MessageSubQueue subQueue, ServiceBusReceivedMessage message, DateTimeOffset now) => subQueue switch
    {
        MessageSubQueue.Scheduled => message.ScheduledEnqueueTime > now,
        MessageSubQueue.Active => message.ScheduledEnqueueTime <= now,
        _ => true,
    };

    private static void ThrowIfScheduled(MessageSubQueue subQueue, string operation)
    {
        if (subQueue == MessageSubQueue.Scheduled)
        {
            throw new NotSupportedByProviderException(
                $"Cannot {operation} scheduled messages — they only become receivable at their scheduled enqueue time. Browse them with peek instead.");
        }
    }

    private static SubQueue MapSubQueue(MessageSubQueue subQueue) => subQueue switch
    {
        MessageSubQueue.DeadLetter => SubQueue.DeadLetter,
        MessageSubQueue.TransferDeadLetter => SubQueue.TransferDeadLetter,
        // Scheduled has no broker sub-queue; scheduled messages come from the regular peek.
        _ => SubQueue.None,
    };

    private static void ApplyOverrides(ServiceBusMessage message, ResubmitOverrides overrides)
    {
        if (overrides.Body is not null)
        {
            message.Body = new BinaryData(Encoding.UTF8.GetBytes(overrides.Body));
        }

        if (overrides.ContentType is not null)
        {
            message.ContentType = overrides.ContentType;
        }

        if (overrides.Subject is not null)
        {
            message.Subject = overrides.Subject;
        }

        if (overrides.MessageId is not null)
        {
            message.MessageId = overrides.MessageId;
        }

        if (overrides.CorrelationId is not null)
        {
            message.CorrelationId = overrides.CorrelationId;
        }

        if (overrides.SessionId is not null)
        {
            message.SessionId = overrides.SessionId;
        }

        if (overrides.ReplyToSessionId is not null)
        {
            message.ReplyToSessionId = overrides.ReplyToSessionId;
        }

        if (overrides.To is not null)
        {
            message.To = overrides.To;
        }

        if (overrides.ReplyTo is not null)
        {
            message.ReplyTo = overrides.ReplyTo;
        }

        if (overrides.TimeToLive.HasValue)
        {
            message.TimeToLive = overrides.TimeToLive.Value;
        }

        if (overrides.ScheduledEnqueueTime.HasValue)
        {
            message.ScheduledEnqueueTime = overrides.ScheduledEnqueueTime.Value;
        }

        if (overrides.ApplicationProperties is not null)
        {
            message.ApplicationProperties.Clear();
            foreach (KeyValuePair<string, object?> property in overrides.ApplicationProperties)
            {
                if (property.Value is not null)
                {
                    message.ApplicationProperties[property.Key] = property.Value;
                }
            }
        }
    }

    /// <param name="SessionKey">The held session the message was received through; null outside sessions.</param>
    private sealed record LockedMessage(
        string EntityPath,
        MessageSubQueue SubQueue,
        ServiceBusReceivedMessage Message,
        DateTimeOffset? RenewUntil,
        string? SessionKey);

    private sealed record HeldSession(
        string Key,
        string EntityPath,
        ServiceBusSessionReceiver Receiver,
        DateTimeOffset? RenewUntil);
}
