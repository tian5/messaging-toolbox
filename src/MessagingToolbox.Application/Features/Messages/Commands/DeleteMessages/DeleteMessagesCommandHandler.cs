using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.DeleteMessages;

public sealed class DeleteMessagesCommandHandler : IRequestHandler<DeleteMessagesCommand, int>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public DeleteMessagesCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<int> Handle(DeleteMessagesCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        int deleted;
        IReadOnlyList<long> affectedSequences;
        if (request.SubQueue == MessageSubQueue.Scheduled)
        {
            // Scheduled messages are never receivable — cancelling the schedule is the only deletion.
            IReadOnlyList<long> cancelled = await provider.CancelScheduledAsync(
                request.EntityPath,
                request.SequenceNumbers,
                cancellationToken);

            deleted = cancelled.Count;
            affectedSequences = cancelled;
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
                    SettlementOperation.Complete,
                    request.LockTokens ?? [],
                    null,
                    null),
                cancellationToken);

            deleted = settled.Count;
            affectedSequences = request.SequenceNumbers;
        }

        if (deleted > 0)
        {
            string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
            await _auditLog.WriteAsync(
                AuditLog.DeleteSelected,
                connectionName,
                request.EntityPath,
                request.SubQueue,
                $"{deleted} of {Math.Max(request.SequenceNumbers.Count, (request.LockTokens ?? []).Count)} selected message(s) deleted (sequences: {AuditSequenceFormatter.Describe(affectedSequences)})",
                cancellationToken);
        }

        return deleted;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
