using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;

public sealed class RequeueMessagesCommandHandler : IRequestHandler<RequeueMessagesCommand, int>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public RequeueMessagesCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<int> Handle(RequeueMessagesCommand request, CancellationToken cancellationToken)
    {
        if (request.Messages.Count == 0)
        {
            return 0;
        }

        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        // Checked before any clone is sent: without lock tokens the originals can only be removed
        // by sequence number, and a requeue that can't remove them would leave duplicates.
        if (request.RemoveOriginals && request.LockTokens is not { Count: > 0 } && !provider.Capabilities.SupportsSequenceNumbers)
        {
            throw new NotSupportedByProviderException(
                "This connection has no sequence numbers; load the messages for settlement to requeue them with removal.");
        }

        List<ToolboxMessage> clones = [];
        foreach (MessageDto message in request.Messages)
        {
            clones.Add(await provider.PrepareResubmitAsync(
                request.SourceEntityPath,
                request.SourceSubQueue,
                MessageDtoMapper.ToToolboxMessage(message),
                ResubmitOverrides.Empty,
                cancellationToken));
        }

        await provider.SendAsync(request.TargetPath, clones, cancellationToken);

        // Clone semantics made explicit (FR-4.6): the originals are settled only on request and
        // only after the copies landed. Two removal paths: the held lock tokens when the messages
        // were received for settlement, otherwise a bounded sequence-number scan (DLQ-only).
        int removed = 0;
        if (request.RemoveOriginals)
        {
            if (request.LockTokens is { Count: > 0 })
            {
                IReadOnlyList<string> settled = await provider.SettleAsync(
                    new SettleRequest(
                        request.SourceEntityPath,
                        request.SourceSubQueue,
                        SettlementOperation.Complete,
                        request.LockTokens,
                        null,
                        null),
                    cancellationToken);

                removed = settled.Count;
            }
            else
            {
                IReadOnlyList<long> removalSequences = request.Messages
                    .Where(message => message.SequenceNumber is not null)
                    .Select(message => message.SequenceNumber!.Value)
                    .ToList();

                if (removalSequences.Count > 0)
                {
                    IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
                        new SettleBySequenceRequest(
                            request.SourceEntityPath,
                            request.SourceSubQueue,
                            SettlementOperation.Complete,
                            removalSequences,
                            null,
                            null),
                        cancellationToken);

                    removed = settled.Count;
                }
            }
        }

        string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
        string sequences = AuditSequenceFormatter.Describe(
            request.Messages
                .Where(message => message.SequenceNumber is not null)
                .Select(message => message.SequenceNumber!.Value)
                .ToList());

        string details = removed > 0
            ? $"Requeued {clones.Count} message(s) to '{request.TargetPath}', {removed} original(s) removed from the DLQ (sequences: {sequences})"
            : $"Requeued {clones.Count} message(s) to '{request.TargetPath}', originals kept (sequences: {sequences})";

        await _auditLog.WriteAsync(
            AuditLog.Requeue,
            connectionName,
            request.SourceEntityPath,
            request.SourceSubQueue,
            details,
            cancellationToken);

        return clones.Count;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
