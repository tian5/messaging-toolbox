using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;

public sealed class SettleMessagesCommandHandler : IRequestHandler<SettleMessagesCommand>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public SettleMessagesCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<Unit> Handle(SettleMessagesCommand request, CancellationToken cancellationToken)
    {
        if (request.LockTokens.Count == 0)
        {
            return Unit.Value;
        }

        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        IReadOnlyList<string> settled = await provider.SettleAsync(
            new SettleRequest(
                request.EntityPath,
                request.SubQueue,
                request.Operation,
                request.LockTokens,
                request.Reason,
                request.Description),
            cancellationToken);

        // Complete and dead-letter remove the message; abandon/defer keep it.
        if (settled.Count > 0 && request.Operation is SettlementOperation.Complete or SettlementOperation.DeadLetter)
        {
            string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
            await _auditLog.WriteAsync(
                request.Operation == SettlementOperation.Complete ? AuditLog.SettleComplete : AuditLog.SettleDeadLetter,
                connectionName,
                request.EntityPath,
                request.SubQueue,
                $"{settled.Count} message(s)" + (request.Reason is null ? string.Empty : $" — {request.Reason}"),
                cancellationToken);
        }

        return Unit.Value;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
