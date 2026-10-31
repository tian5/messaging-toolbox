using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Management.Queries.GetRoutingRules;

/// <summary>Routing rules from or to an entity, with the connection's rule type (ADR 22).</summary>
public sealed record GetRoutingRulesQuery(Guid ConnectionId, string EntityPath) : IRequest<RoutingRulesDto>;
