using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Management.Commands.DeleteRoutingRule;

/// <summary>Deletes a routing rule obtained from <c>GetRoutingRulesQuery</c> (ADR 22).</summary>
public sealed record DeleteRoutingRuleCommand(Guid ConnectionId, RoutingRule Rule) : IRequest;
