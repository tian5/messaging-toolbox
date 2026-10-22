using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateRoutingRule;

public sealed class CreateRoutingRuleCommandHandler : IRequestHandler<CreateRoutingRuleCommand, RoutingRule>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public CreateRoutingRuleCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<RoutingRule> Handle(CreateRoutingRuleCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);
        if (!provider.Capabilities.CanManageRules)
        {
            throw new NotSupportedByProviderException("This connection can't manage routing rules.");
        }

        RoutingRule rule = await provider.CreateRoutingRuleAsync(request.SourcePath, request.DestinationPath, request.Options, cancellationToken);

        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.CreateRoutingRule,
            connection?.Name ?? request.ConnectionId.ToString(),
            request.SourcePath,
            MessageSubQueue.Active,
            $"Routes to {request.DestinationPath}",
            cancellationToken);

        return rule;
    }
}
