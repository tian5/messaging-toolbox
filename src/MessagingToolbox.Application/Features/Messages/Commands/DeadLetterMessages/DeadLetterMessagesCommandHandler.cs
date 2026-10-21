using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;

public sealed class DeadLetterMessagesCommandHandler : IRequestHandler<DeadLetterMessagesCommand, int>
{
    /// <summary>Correlates a re-enqueued scheduled message with the copy we have to dead-letter.</summary>
    private const string TransferMarkerProperty = "toolbox-dlq-transfer";

    private const string DefaultReason = "Moved to dead-letter from MessagingToolbox";

    private const int MaxScheduledPeek = 100;

    private const int MaxMarkerScanRounds = 10;

    private static readonly TimeSpan MarkerScanWaitTime = TimeSpan.FromSeconds(5);

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public DeadLetterMessagesCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<int> Handle(DeadLetterMessagesCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);
        string reason = request.Reason ?? DefaultReason;

        int deadLettered;
        IReadOnlyList<long> affectedSequences;
        if (request.SubQueue == MessageSubQueue.Scheduled)
        {
            deadLettered = await DeadLetterScheduledAsync(provider, request, reason, cancellationToken);
            affectedSequences = request.SequenceNumbers;
        }
        else
        {
            if (request.LockTokens is not { Count: > 0 })
            {
                return 0;
            }

            IReadOnlyList<string> settled = await provider.SettleAsync(
                new SettleRequest(
                    request.EntityPath,
                    request.SubQueue,
                    SettlementOperation.DeadLetter,
                    request.LockTokens ?? [],
                    reason,
                    request.Description),
                cancellationToken);

            deadLettered = settled.Count;
            affectedSequences = request.SequenceNumbers;
        }

        if (deadLettered > 0)
        {
            string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
            await _auditLog.WriteAsync(
                AuditLog.DeadLetterSelected,
                connectionName,
                request.EntityPath,
                request.SubQueue,
                $"{deadLettered} of {Math.Max(request.SequenceNumbers.Count, (request.LockTokens ?? []).Count)} selected message(s) dead-lettered (sequences: {AuditSequenceFormatter.Describe(affectedSequences)}) — {reason}",
                cancellationToken);
        }

        return deadLettered;
    }

    /// <summary>
    /// A scheduled message is not receivable, so it cannot be dead-lettered directly. The schedule is
    /// cancelled and the message re-sent for immediate delivery carrying a marker property, then
    /// located in the active sub-queue and dead-lettered. The copies are briefly active - a
    /// competing consumer may take them first.
    /// </summary>
    private static async Task<int> DeadLetterScheduledAsync(
        IMessagingProvider provider,
        DeadLetterMessagesCommand request,
        string reason,
        CancellationToken cancellationToken)
    {
        HashSet<long> targets = [.. request.SequenceNumbers];

        PeekRequest peekRequest = new(request.EntityPath, MessageSubQueue.Scheduled, MaxScheduledPeek, null);
        IReadOnlyList<ToolboxMessage> scheduled = await provider.PeekAsync(peekRequest, cancellationToken);

        List<ToolboxMessage> selected = scheduled
            .Where(message => message.SequenceNumber is not null && targets.Contains(message.SequenceNumber.Value))
            .ToList();

        if (selected.Count == 0)
        {
            return 0;
        }

        string marker = Guid.NewGuid().ToString("N");

        IReadOnlyList<long> cancelled = await provider.CancelScheduledAsync(
            request.EntityPath,
            selected.Select(message => message.SequenceNumber!.Value).ToList(),
            cancellationToken);

        if (cancelled.Count == 0)
        {
            return 0;
        }

        // Re-send only the schedules that were actually cancelled — a MessageNotFound skip must
        // never replay a message that is still scheduled.
        HashSet<long> cancelledSet = [.. cancelled];
        List<ToolboxMessage> replays = selected
            .Where(message => cancelledSet.Contains(message.SequenceNumber!.Value))
            .Select(message => Mark(message, marker))
            .ToList();

        await provider.SendAsync(request.EntityPath, replays, cancellationToken);

        return await DeadLetterMarkedCopiesAsync(provider, request.EntityPath, marker, replays.Count, reason, request.Description, cancellationToken);
    }

    // The re-sent copies land in the active sub-queue asynchronously among other messages.
    // Receive for settlement in bounded rounds: marker copies are dead-lettered, everything
    // else is abandoned back (never deferred — nothing can strand). Stops when every copy is
    // accounted for, a round finds nothing, or the round budget runs out; unfound copies were
    // taken by a competing consumer and are reported as not moved.
    private static async Task<int> DeadLetterMarkedCopiesAsync(
        IMessagingProvider provider,
        string entityPath,
        string marker,
        int expected,
        string reason,
        string? description,
        CancellationToken cancellationToken)
    {
        int deadLettered = 0;

        for (int round = 0; round < MaxMarkerScanRounds && deadLettered < expected; round++)
        {
            IReadOnlyList<ToolboxMessage> received =
                await provider.ReceiveForSettlementAsync(entityPath, MessageSubQueue.Active, MaxScheduledPeek, MarkerScanWaitTime, cancellationToken);

            if (received.Count == 0)
            {
                break;
            }

            List<string> markerTokens = [];
            List<string> otherTokens = [];
            foreach (ToolboxMessage message in received)
            {
                if (Marker(message) == marker && message.LockToken is not null)
                {
                    markerTokens.Add(message.LockToken);
                }
                else if (message.LockToken is not null)
                {
                    otherTokens.Add(message.LockToken);
                }
            }

            if (markerTokens.Count > 0)
            {
                IReadOnlyList<string> settled = await provider.SettleAsync(
                    new SettleRequest(entityPath, MessageSubQueue.Active, SettlementOperation.DeadLetter, markerTokens, reason, description),
                    cancellationToken);

                deadLettered += settled.Count;
            }

            if (otherTokens.Count > 0)
            {
                await provider.SettleAsync(
                    new SettleRequest(entityPath, MessageSubQueue.Active, SettlementOperation.Abandon, otherTokens, null, null),
                    cancellationToken);
            }
        }

        return deadLettered;
    }

    private static ToolboxMessage Mark(ToolboxMessage message, string marker)
    {
        Dictionary<string, object?> properties = new(message.ApplicationProperties)
        {
            [TransferMarkerProperty] = marker,
        };

        return message with
        {
            ScheduledEnqueueTime = null,
            SequenceNumber = null,
            LockToken = null,
            ApplicationProperties = properties,
        };
    }

    private static string? Marker(ToolboxMessage message)
        => message.ApplicationProperties.TryGetValue(TransferMarkerProperty, out object? value)
            ? value?.ToString()
            : null;

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
