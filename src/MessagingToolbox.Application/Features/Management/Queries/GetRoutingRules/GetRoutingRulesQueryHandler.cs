using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Management.Queries.GetRoutingRules;

public sealed class GetRoutingRulesQueryHandler : IRequestHandler<GetRoutingRulesQuery, RoutingRulesDto>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetRoutingRulesQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<RoutingRulesDto> Handle(GetRoutingRulesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        // Listing needs the same management plane as changing rules, so both hang on the capability.
        if (provider.GetRoutingRuleType() is not { } type || !provider.Capabilities.CanManageRules)
        {
            return RoutingRulesDto.None;
        }

        IReadOnlyList<RoutingRule> rules = await provider.ListRoutingRulesAsync(request.EntityPath, cancellationToken);
        return new RoutingRulesDto(type, CanManage: true, rules);
    }
}
