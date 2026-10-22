using System.Collections.Concurrent;
using System.Globalization;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// Settle-later receives on RabbitMQ (ADR 19). A delivery fetched without auto-ack stays held by
/// the channel that received it — there is no lock that expires — so each queue gets one channel
/// that is kept open while it holds deliveries. Lock tokens are <c>{heldId}:{deliveryTag}</c>: the
/// random id makes a token of a closed (and possibly reopened) channel fail cleanly instead of
/// settling someone else's delivery.
/// Held deliveries block the application under test, so a channel left idle for
/// <see cref="IdleTimeout"/> is released. Releasing always nacks with requeue before closing: a
/// channel closing with unacked deliveries counts as a failed delivery on quorum queues.
/// </summary>
internal sealed class HeldDeliveries : IAsyncDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, HeldChannel> _byQueue = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ITimer _sweepTimer;

    public HeldDeliveries(TimeProvider timeProvider, ILogger logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _sweepTimer = timeProvider.CreateTimer(_ => _ = ReleaseIdleAsync(), null, SweepInterval, SweepInterval);
    }

    /// <summary>Gets up to <paramref name="maxMessages"/> deliveries from <paramref name="queue"/> and holds them.</summary>
    public async Task<IReadOnlyList<(string LockToken, BasicGetResult Delivery)>> ReceiveAsync(
        string queue,
        Func<CancellationToken, Task<IChannel>> openChannel,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        HeldChannel held = await GetOrOpenAsync(queue, openChannel, cancellationToken);
        await held.Gate.WaitAsync(cancellationToken);
        try
        {
            List<(string, BasicGetResult)> received = [];
            while (received.Count < maxMessages
                && await held.Channel.BasicGetAsync(queue, autoAck: false, cancellationToken) is { } delivery)
            {
                held.Tags.Add(delivery.DeliveryTag);
                received.Add((held.TokenFor(delivery.DeliveryTag), delivery));
            }

            held.Touch(_timeProvider.GetUtcNow());
            return received;
        }
        finally
        {
            held.Gate.Release();
        }
    }

    /// <summary>
    /// Settles the held deliveries behind <paramref name="lockTokens"/> and returns the tokens that
    /// were settled. Throws <see cref="MessageLockLostException"/> when none of them is held anymore.
    /// </summary>
    public async Task<IReadOnlyList<string>> SettleAsync(
        IReadOnlyList<string> lockTokens, SettlementOperation operation, CancellationToken cancellationToken)
    {
        if (operation is not (SettlementOperation.Complete or SettlementOperation.Abandon or SettlementOperation.DeadLetter))
        {
            throw new NotSupportedByProviderException($"RabbitMQ can't {operation} a message.");
        }

        List<string> settled = [];
        foreach (IGrouping<Guid, (Guid HeldId, ulong Tag, string Token)> group in lockTokens
            .Select(Parse)
            .Where(token => token is not null)
            .Select(token => token!.Value)
            .GroupBy(token => token.HeldId))
        {
            if (FindHeld(group.Key) is not { } held)
            {
                continue;
            }

            await held.Gate.WaitAsync(cancellationToken);
            try
            {
                foreach ((_, ulong tag, string token) in group)
                {
                    if (!held.Channel.IsOpen || !held.Tags.Remove(tag))
                    {
                        continue;
                    }

                    await (operation switch
                    {
                        SettlementOperation.Complete => held.Channel.BasicAckAsync(tag, multiple: false, cancellationToken),
                        SettlementOperation.Abandon => held.Channel.BasicNackAsync(tag, multiple: false, requeue: true, cancellationToken),
                        _ => held.Channel.BasicRejectAsync(tag, requeue: false, cancellationToken),
                    });
                    settled.Add(token);
                }

                held.Touch(_timeProvider.GetUtcNow());
            }
            finally
            {
                held.Gate.Release();
            }

            // Nothing left to hold: close the channel (no unacked deliveries, so a clean close).
            if (held.Tags.Count == 0)
            {
                await ReleaseAsync(held);
            }
        }

        if (settled.Count == 0 && lockTokens.Count > 0)
        {
            throw new MessageLockLostException(
                "The held delivery is gone — its channel was closed (released, idle for too long, or closed by the broker), which requeued the message. Receive it again.");
        }

        return settled;
    }

    /// <summary>The tokens of <paramref name="lockTokens"/> that are still held.</summary>
    public IReadOnlyList<string> StillHeld(IReadOnlyList<string> lockTokens) => lockTokens
        .Where(token => Parse(token) is { } parsed
            && FindHeld(parsed.HeldId) is { Channel.IsOpen: true } held
            && held.Tags.Contains(parsed.Tag))
        .ToList();

    /// <summary>Releases (requeues) every channel that held deliveries without settling any for <see cref="IdleTimeout"/>.</summary>
    internal async Task ReleaseIdleAsync()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (HeldChannel held in _byQueue.Values.Where(held => now - held.LastActivity >= IdleTimeout).ToList())
        {
            _logger.LogInformation("Releasing {Count} idle held RabbitMQ deliveries on {Queue}.", held.Tags.Count, held.Queue);
            await ReleaseAsync(held);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sweepTimer.DisposeAsync();
        foreach (HeldChannel held in _byQueue.Values.ToList())
        {
            await ReleaseAsync(held);
        }
    }

    private static (Guid HeldId, ulong Tag, string Token)? Parse(string token)
    {
        int separator = token.IndexOf(':', StringComparison.Ordinal);
        return separator > 0
            && Guid.TryParseExact(token[..separator], "N", out Guid heldId)
            && ulong.TryParse(token[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out ulong tag)
            ? (heldId, tag, token)
            : null;
    }

    private HeldChannel? FindHeld(Guid heldId) => _byQueue.Values.FirstOrDefault(held => held.Id == heldId);

    private async Task<HeldChannel> GetOrOpenAsync(
        string queue, Func<CancellationToken, Task<IChannel>> openChannel, CancellationToken cancellationToken)
    {
        if (_byQueue.TryGetValue(queue, out HeldChannel? existing))
        {
            if (existing.Channel.IsOpen)
            {
                return existing;
            }

            // Closed by the broker (consumer timeout, restart): its deliveries are back in the queue.
            await ReleaseAsync(existing);
        }

        HeldChannel held = new(queue, await openChannel(cancellationToken), _timeProvider.GetUtcNow());
        _byQueue[queue] = held;
        return held;
    }

    private async Task ReleaseAsync(HeldChannel held)
    {
        if (!_byQueue.TryRemove(new KeyValuePair<string, HeldChannel>(held.Queue, held)))
        {
            return;
        }

        await held.Gate.WaitAsync();
        try
        {
            if (held.Channel.IsOpen && held.Tags.Count > 0)
            {
                await held.Channel.BasicNackAsync(held.Tags.Max(), multiple: true, requeue: true);
            }

            held.Tags.Clear();
            await held.Channel.CloseAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Releasing held RabbitMQ deliveries on {Queue} failed; the broker requeues them as the channel closes.", held.Queue);
        }
        finally
        {
            held.Gate.Release();
            await held.Channel.DisposeAsync();
        }
    }

    private sealed class HeldChannel(string queue, IChannel channel, DateTimeOffset now)
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Queue { get; } = queue;

        public IChannel Channel { get; } = channel;

        public HashSet<ulong> Tags { get; } = [];

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTimeOffset LastActivity { get; private set; } = now;

        public void Touch(DateTimeOffset now) => LastActivity = now;

        public string TokenFor(ulong deliveryTag) => $"{Id:N}:{deliveryTag.ToString(CultureInfo.InvariantCulture)}";
    }
}
