using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Management.Commands.DeleteRoutingRule;

public sealed class DeleteRoutingRuleCommandHandler : IRequestHandler<DeleteRoutingRuleCommand>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public DeleteRoutingRuleCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<Unit> Handle(DeleteRoutingRuleCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);
        if (!provider.Capabilities.CanManageRules)
        {
            throw new NotSupportedByProviderException("This connection can't manage routing rules.");
        }

        await provider.DeleteRoutingRuleAsync(request.Rule, cancellationToken);

        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.DeleteRoutingRule,
            connection?.Name ?? request.ConnectionId.ToString(),
            request.Rule.SourcePath,
            MessageSubQueue.Active,
            $"No longer routes to {request.Rule.DestinationPath} ({request.Rule.Id})",
            cancellationToken);

        return Unit.Value;
    }
}
