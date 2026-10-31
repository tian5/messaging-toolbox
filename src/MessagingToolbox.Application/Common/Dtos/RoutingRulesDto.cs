using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>
/// The routing rules of one entity and what the connection allows with them (ADR 22).
/// <see cref="Type"/> is null when the connection has no routing rules at all.
/// </summary>
public sealed record RoutingRulesDto(RoutingRuleType? Type, bool CanManage, IReadOnlyList<RoutingRule> Rules)
{
    public static RoutingRulesDto None { get; } = new(null, false, []);
}
