namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// The routing rules a provider manages (ADR 22): which entity kinds a rule connects and the
/// options it takes, so the UI can offer them without knowing the broker. A RabbitMQ binding
/// routes from an exchange to a queue or exchange by routing key and match arguments.
/// </summary>
/// <param name="Label">Human-readable name, lower case ("binding").</param>
/// <param name="SourceKinds">Kinds a rule can route from.</param>
/// <param name="DestinationKinds">Kinds a rule can route to.</param>
/// <param name="Options">Settings of a rule, in display order; values typed as in <see cref="EntityDefinition.Options"/>.</param>
public sealed record RoutingRuleType(
    string Label,
    IReadOnlyList<EntityKind> SourceKinds,
    IReadOnlyList<EntityKind> DestinationKinds,
    IReadOnlyList<EntityOptionDescriptor> Options)
{
    /// <summary>Help text for the rule type.</summary>
    public string? Description { get; init; }
}
