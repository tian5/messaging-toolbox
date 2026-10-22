using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Configuration;
using MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;
using Microsoft.Extensions.Options;

namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// Per-circuit live watch: entities the user pinned are peeked on an interval for messages that
/// arrived since the last poll. Peek only — nothing is locked or received, so watching never
/// changes what the user's application sees. Nothing is watched unless the user pins it, and the
/// arrivals go to the watch panel, never into the message grid.
/// </summary>
public sealed class LiveWatchService : IDisposable
{
    private const int PeekBatchSize = 500;

    // The first walk skips what is already there; a deep queue takes a few ticks to catch up.
    private const int CatchUpBatchesPerPoll = 10;

    private const int BatchesPerPoll = 2;

    // Dead-letter views are peeked whole each poll (the provider's peek limit).
    private const int MaxWalkedMessages = 5000;

    private readonly ISender _sender;
    private readonly ILogger<LiveWatchService> _logger;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly CancellationTokenSource _disposal = new();

    private IReadOnlyList<LiveWatch> _watches = [];
    private PeriodicTimer? _timer;

    public LiveWatchService(ISender sender, IOptions<ToolboxOptions> options, ILogger<LiveWatchService> logger)
    {
        _sender = sender;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.WatchIntervalSeconds));
    }

    /// <summary>Raised after a poll changed a watch, and after pin/unpin/clear.</summary>
    public event Func<Task>? Changed;

    public IReadOnlyList<LiveWatch> Watches => _watches;

    public bool IsWatched(Guid connectionId, string path, MessageSubQueue subQueue)
        => _watches.Any(watch => watch.ConnectionId == connectionId && watch.Path == path && watch.SubQueue == subQueue);

    /// <summary>Pin the entity, or unpin it when it is already watched.</summary>
    public void Toggle(Guid connectionId, string connectionName, string path, MessageSubQueue subQueue)
    {
        LiveWatch? existing = _watches.FirstOrDefault(watch => watch.ConnectionId == connectionId && watch.Path == path && watch.SubQueue == subQueue);
        if (existing is not null)
        {
            Unpin(existing);
            return;
        }

        _watches = [.. _watches, new LiveWatch { ConnectionId = connectionId, ConnectionName = connectionName, Path = path, SubQueue = subQueue }];
        EnsureRunning();
        PollSoon();
        _ = RaiseChangedAsync();
    }

    public void Unpin(LiveWatch watch)
    {
        _watches = [.. _watches.Where(candidate => candidate.Key != watch.Key)];
        if (_watches.Count == 0)
        {
            StopTimer();
        }

        _ = RaiseChangedAsync();
    }

    /// <summary>Forget the arrivals shown so far; the watch keeps going from where it is.</summary>
    public void Clear(LiveWatch watch)
    {
        watch.Arrivals = [];
        watch.ArrivalCount = 0;
        watch.SinceLastSend = new HashSet<long>();
        _ = RaiseChangedAsync();
    }

    /// <summary>Drop the watches of connections that no longer exist.</summary>
    public void PruneExcept(IEnumerable<Guid> connectionIds)
    {
        HashSet<Guid> valid = [.. connectionIds];
        if (_watches.All(watch => valid.Contains(watch.ConnectionId)))
        {
            return;
        }

        _watches = [.. _watches.Where(watch => valid.Contains(watch.ConnectionId))];
        if (_watches.Count == 0)
        {
            StopTimer();
        }
    }

    /// <summary>
    /// The user just sent on <paramref name="connectionId"/>: arrivals from here on are
    /// highlighted as "since last send", and the watches poll right away instead of on the next tick.
    /// </summary>
    public void MarkSent(Guid connectionId)
    {
        foreach (LiveWatch watch in _watches.Where(watch => watch.ConnectionId == connectionId))
        {
            watch.SinceLastSend = new HashSet<long>();
        }

        PollSoon();
        _ = RaiseChangedAsync();
    }

    public void Dispose()
    {
        _disposal.Cancel();
        StopTimer();
        _disposal.Dispose();
    }

    private void EnsureRunning()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new PeriodicTimer(_interval);
        _ = RunLoopAsync(_timer);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void PollSoon() => _ = PollAsync();

    private async Task RunLoopAsync(PeriodicTimer timer)
    {
        try
        {
            // A disposed timer ends the loop (WaitForNextTickAsync returns false).
            while (await timer.WaitForNextTickAsync(_disposal.Token))
            {
                await PollAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Circuit disposed.
        }
    }

    // One poll at a time: a tick landing while the previous poll (or a send-triggered one) is
    // still running is skipped, so a slow broker never piles up peeks.
    private async Task PollAsync()
    {
        if (!await _pollGate.WaitAsync(0))
        {
            return;
        }

        bool changed = false;
        try
        {
            foreach (LiveWatch watch in _watches)
            {
                changed |= await PollWatchAsync(watch);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        finally
        {
            ReleaseGate();
        }

        if (changed)
        {
            await RaiseChangedAsync();
        }
    }

    private void ReleaseGate()
    {
        try
        {
            _pollGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed mid-poll.
        }
    }

    private async Task<bool> PollWatchAsync(LiveWatch watch)
    {
        string? previousError = watch.Error;
        bool wasReady = watch.IsReady;
        List<MessageDto> arrived = [];

        try
        {
            arrived = watch.WalksWholeView
                ? await WalkWholeViewAsync(watch)
                : await AdvanceCursorAsync(watch);
            watch.Error = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
        {
            watch.Error = exception.Message;
            _logger.LogDebug(exception, "Live watch poll of {Path} ({SubQueue}) failed", watch.Path, watch.SubQueue);
        }

        if (arrived.Count > 0)
        {
            watch.Arrivals = [.. arrived.OrderByDescending(message => message.SequenceNumber).Concat(watch.Arrivals).Take(LiveWatch.MaxKept)];
            watch.ArrivalCount += arrived.Count;
            watch.SinceLastSend = new HashSet<long>(watch.SinceLastSend.Concat(arrived.Select(message => message.SequenceNumber!.Value)));
        }

        return arrived.Count > 0 || watch.IsReady != wasReady || watch.Error != previousError;
    }

    // Active and scheduled views: peek past the cursor. Nothing found before the first walk
    // reaches the end counts as an arrival — it was there before the pin.
    private async Task<List<MessageDto>> AdvanceCursorAsync(LiveWatch watch)
    {
        List<MessageDto> arrived = [];
        int batches = watch.IsReady ? BatchesPerPoll : CatchUpBatchesPerPoll;
        bool reachedEnd = false;
        for (int batch = 0; batch < batches && !reachedEnd; batch++)
        {
            IReadOnlyList<MessageDto> peeked = await _sender.Send(
                new PeekMessagesQuery(watch.ConnectionId, watch.Path, watch.SubQueue, watch.LastSeenSequence + 1, PeekBatchSize),
                _disposal.Token);

            List<MessageDto> fresh = peeked
                .Where(message => message.SequenceNumber is { } sequence && (watch.LastSeenSequence is not { } last || sequence > last))
                .ToList();
            if (fresh.Count > 0)
            {
                watch.LastSeenSequence = fresh.Max(message => message.SequenceNumber!.Value);
            }

            if (watch.IsReady)
            {
                arrived.AddRange(fresh);
            }

            reachedEnd = peeked.Count < PeekBatchSize;
        }

        watch.IsReady |= reachedEnd;
        return arrived;
    }

    // Dead-letter views: whatever is present now and wasn't at the last walk arrived since.
    private async Task<List<MessageDto>> WalkWholeViewAsync(LiveWatch watch)
    {
        IReadOnlyList<MessageDto> present = await _sender.Send(
            new PeekMessagesQuery(watch.ConnectionId, watch.Path, watch.SubQueue, null, MaxWalkedMessages),
            _disposal.Token);

        List<MessageDto> arrived = watch.IsReady
            ? [.. present.Where(message => message.SequenceNumber is { } sequence && !watch.SeenSequences.Contains(sequence))]
            : [];

        watch.SeenSequences = present
            .Where(message => message.SequenceNumber is not null)
            .Select(message => message.SequenceNumber!.Value)
            .ToHashSet();
        watch.IsTruncated = present.Count >= MaxWalkedMessages;
        watch.IsReady = true;
        return arrived;
    }

    private async Task RaiseChangedAsync()
    {
        if (Changed is null)
        {
            return;
        }

        // Each subscriber (the panel, the page's pin buttons) on its own, so one failing
        // handler neither hides the others nor leaves their tasks unobserved.
        foreach (Func<Task> handler in Changed.GetInvocationList().Cast<Func<Task>>())
        {
            try
            {
                await handler();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Live watch change handler failed");
            }
        }
    }
}
