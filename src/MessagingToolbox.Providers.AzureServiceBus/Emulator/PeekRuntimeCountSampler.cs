using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus.Emulator;

/// <summary>
/// <see cref="IEmulatorRuntimeCountSampler"/> over bounded peeks. The broker serves at most
/// <see cref="PageSize"/> messages per peek, so sub-queues are counted by paging through them up
/// to <see cref="SampleCap"/> messages (NFR-2: still bounded) — counts below the cap are exact.
/// One active peek doubles as the scheduled sample: scheduled messages show up in regular queue
/// peeks with a future ScheduledEnqueueTime. Deferred messages show up there too: they stay in the
/// active count and are reported separately as well. Only counts that hit the cap are flagged approximate.
/// <para>
/// Sampling is expensive (up to two full sub-queue walks per entity), so results are cached for
/// <see cref="CacheTtl"/> and must be invalidated after mutations, and concurrent calls for the
/// same entity coalesce onto a single in-flight walk. Receivers are NOT cached across walks: on
/// the emulator a reused receiver's cursor-less peek resumes where the previous walk ended
/// instead of at the head, silently undercounting (verified live) — each walk opens and
/// disposes its own receiver.
/// </para>
/// <para>
/// Invalidation bumps a per-entity generation: a walk that started before the mutation still
/// answers its own callers but never caches its (stale) result, and later callers start a fresh
/// walk. Walks run on the sampler's lifetime token, not a caller's, so one cancelled caller never
/// cancels the others sharing the walk.
/// </para>
/// <para>
/// The emulator copies a topic's messages into its subscriptions asynchronously (verified live:
/// tens of milliseconds), so a subscription sampled right after a send to its topic still shows
/// the old count and would cache it. After a topic is invalidated, subscription walks of that
/// topic wait out <see cref="FanOutSettleTime"/> from the invalidation before peeking.
/// </para>
/// </summary>
public sealed class PeekRuntimeCountSampler : IEmulatorRuntimeCountSampler, IAsyncDisposable
{
    private const int PageSize = 100;

    public const int SampleCap = 20_000;

    // Short enough that the 10 s auto-refresh fallback and manual refreshes see fresh counts;
    // long enough to absorb the burst of a tree load plus details-pane lookups.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    internal static readonly TimeSpan FanOutSettleTime = TimeSpan.FromMilliseconds(500);

    private readonly ServiceBusClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedSample> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<EntityRuntimeInfo>>> _inFlight = new();
    private readonly ConcurrentDictionary<string, long> _generations = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _fanOutSettlesAt = new();
    private readonly CancellationTokenSource _disposalTokenSource = new();
    private volatile bool _disposed;

    public PeekRuntimeCountSampler(ServiceBusClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
        ReceiverFactory = CreateReceiver;
    }

    /// <summary>
    /// Receiver creation seam: unit tests substitute the SDK receiver so the sampling walk runs
    /// without a broker. Production keeps the <see cref="ServiceBusClient"/> default.
    /// </summary>
    internal Func<string, string?, SubQueue, ServiceBusReceiver> ReceiverFactory { get; set; }

    public async Task<EntityRuntimeInfo> SampleAsync(string entityPath, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cache.TryGetValue(entityPath, out CachedSample? cached) && cached.ExpiresAt > _timeProvider.GetUtcNow())
        {
            return cached.Info;
        }

        // Coalesce concurrent misses for the same entity onto one walk.
        Lazy<Task<EntityRuntimeInfo>> walk = _inFlight.GetOrAdd(entityPath, CreateWalk);
        return await walk.Value.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Drops cached counts for <paramref name="entityPath"/> and, for a topic, for all of its
    /// subscriptions — a message sent to a topic lands in every subscription.
    /// </summary>
    public void Invalidate(string entityPath)
    {
        if (ServiceBusEntityPath.Split(entityPath).Subscription is null)
        {
            // Harmless for a queue: only subscription walks look it up.
            _fanOutSettlesAt[entityPath] = _timeProvider.GetUtcNow() + FanOutSettleTime;
        }

        HashSet<string> affected = [entityPath];
        affected.UnionWith(_cache.Keys.Where(key => IsSubscriptionOf(key, entityPath)));
        affected.UnionWith(_inFlight.Keys.Where(key => IsSubscriptionOf(key, entityPath)));

        foreach (string path in affected)
        {
            _generations.AddOrUpdate(path, 1, (_, generation) => generation + 1);
            _cache.TryRemove(path, out _);
            _inFlight.TryRemove(path, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _disposalTokenSource.CancelAsync();
        _disposalTokenSource.Dispose();
        _cache.Clear();
        _inFlight.Clear();
        // Receivers are transient (created and disposed per walk); the client is caller-owned.
    }

    private static bool IsSubscriptionOf(string candidate, string topicPath)
        => ServiceBusEntityPath.Split(candidate) is (string path, not null) && path == topicPath;

    private Lazy<Task<EntityRuntimeInfo>> CreateWalk(string entityPath)
    {
        long generation = _generations.GetValueOrDefault(entityPath);
        Lazy<Task<EntityRuntimeInfo>>? walk = null;
        walk = new Lazy<Task<EntityRuntimeInfo>>(
            () => RunWalkAsync(entityPath, generation, walk!),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return walk;
    }

    private async Task<EntityRuntimeInfo> RunWalkAsync(string entityPath, long generation, Lazy<Task<EntityRuntimeInfo>> walk)
    {
        try
        {
            await WaitForFanOutAsync(entityPath, _disposalTokenSource.Token);
            EntityRuntimeInfo info = await SampleCoreAsync(entityPath, _disposalTokenSource.Token);

            // An invalidation during the walk means the result may predate the mutation.
            if (_generations.GetValueOrDefault(entityPath) == generation)
            {
                _cache[entityPath] = new CachedSample(info, _timeProvider.GetUtcNow() + CacheTtl);
            }

            return info;
        }
        finally
        {
            // Remove only this walk: a newer one may already be registered after an invalidation.
            _inFlight.TryRemove(KeyValuePair.Create(entityPath, walk));
        }
    }

    private async Task WaitForFanOutAsync(string entityPath, CancellationToken cancellationToken)
    {
        if (ServiceBusEntityPath.Split(entityPath) is (string topic, not null)
            && _fanOutSettlesAt.TryGetValue(topic, out DateTimeOffset settlesAt))
        {
            TimeSpan remaining = settlesAt - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, _timeProvider, cancellationToken);
            }
        }
    }

    private async Task<EntityRuntimeInfo> SampleCoreAsync(string entityPath, CancellationToken cancellationToken)
    {
        (string path, string? subscription) = ServiceBusEntityPath.Split(entityPath);

        List<ServiceBusReceivedMessage> activeSample = await PeekAllAsync(path, subscription, SubQueue.None, cancellationToken);
        List<ServiceBusReceivedMessage> deadLetterSample = await PeekAllAsync(path, subscription, SubQueue.DeadLetter, cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        long scheduledCount = activeSample.Count(message => message.ScheduledEnqueueTime > now);

        return new EntityRuntimeInfo
        {
            ActiveMessageCount = activeSample.Count - scheduledCount,
            DeferredMessageCount = activeSample.Count(message => message.State == ServiceBusMessageState.Deferred),
            ScheduledMessageCount = scheduledCount,
            DeadLetterMessageCount = deadLetterSample.Count,
            IsApproximate = activeSample.Count >= SampleCap || deadLetterSample.Count >= SampleCap,
        };
    }

    // A single peek never returns more than a page, so the sub-queue is walked page by page from
    // the last sequence number until it is exhausted or the cap is reached. The receiver is
    // scoped to this walk and disposed at the end — see class remarks for why reuse undercounts.
    private async Task<List<ServiceBusReceivedMessage>> PeekAllAsync(
        string path,
        string? subscription,
        SubQueue subQueue,
        CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = ReceiverFactory(path, subscription, subQueue);

        List<ServiceBusReceivedMessage> sampled = new(PageSize);
        HashSet<long> seen = [];
        long previousMax = long.MinValue;

        while (sampled.Count < SampleCap)
        {
            long? fromSequence = previousMax == long.MinValue ? null : previousMax + 1;
            IReadOnlyList<ServiceBusReceivedMessage> batch =
                await receiver.PeekMessagesAsync(PageSize, fromSequence, cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            int added = 0;
            long maxSequence = long.MinValue;
            foreach (ServiceBusReceivedMessage message in batch)
            {
                maxSequence = Math.Max(maxSequence, message.SequenceNumber);
                if (seen.Add(message.SequenceNumber))
                {
                    sampled.Add(message);
                    added++;
                }
            }

            // A cursor that stops advancing (or repeats a page) means the sub-queue is done.
            if (added == 0 || maxSequence <= previousMax)
            {
                break;
            }

            previousMax = maxSequence;
        }

        return sampled;
    }

    private ServiceBusReceiver CreateReceiver(string path, string? subscription, SubQueue subQueue)
    {
        ServiceBusReceiverOptions options = new() { SubQueue = subQueue };
        return subscription is null
            ? _client.CreateReceiver(path, options)
            : _client.CreateReceiver(path, subscription, options);
    }

    private sealed record CachedSample(EntityRuntimeInfo Info, DateTimeOffset ExpiresAt);
}
