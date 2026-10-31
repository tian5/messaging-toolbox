namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>An existing routing rule from <see cref="SourcePath"/> to <see cref="DestinationPath"/> (ADR 22).</summary>
/// <param name="Id">The broker's identity of the rule among those between the same two entities; pass it back to delete it.</param>
/// <param name="SourcePath">Entity the messages come from.</param>
/// <param name="DestinationPath">Entity the messages go to.</param>
/// <param name="Options">The rule's settings, keyed and typed by <see cref="RoutingRuleType.Options"/>.</param>
public sealed record RoutingRule(
    string Id,
    string SourcePath,
    string DestinationPath,
    IReadOnlyDictionary<string, object?> Options);
