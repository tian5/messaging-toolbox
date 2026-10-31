using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateRoutingRule;

/// <summary>Creates a routing rule (a RabbitMQ binding) from one entity to another (ADR 22).</summary>
public sealed record CreateRoutingRuleCommand(
    Guid ConnectionId,
    string SourcePath,
    string DestinationPath,
    IReadOnlyDictionary<string, object?> Options) : IRequest<RoutingRule>;
