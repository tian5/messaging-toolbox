using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.SettleBySequence;

public sealed class SettleBySequenceCommandHandler : IRequestHandler<SettleBySequenceCommand, int>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public SettleBySequenceCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<int> Handle(SettleBySequenceCommand request, CancellationToken cancellationToken)
    {
        if (request.SequenceNumbers.Count == 0)
        {
            return 0;
        }

        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest(
                request.EntityPath,
                request.SubQueue,
                request.Operation,
                request.SequenceNumbers,
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
                $"{settled.Count} of {request.SequenceNumbers.Count} selected message(s) settled by sequence (sequences: {AuditSequenceFormatter.Describe(settled)})" + (request.Reason is null ? string.Empty : $" — {request.Reason}"),
                cancellationToken);
        }

        return settled.Count;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
