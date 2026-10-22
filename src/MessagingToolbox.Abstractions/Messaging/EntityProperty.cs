namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// One broker-specific setting of an entity, already formatted for display (lock duration, TTL,
/// max size, RabbitMQ arguments, …). Display-only: the core renders it and never branches on it,
/// so providers can surface any detail without widening the contract.
/// </summary>
/// <param name="Group">Section heading the property is listed under, e.g. "Settings" or "Arguments".</param>
/// <param name="Name">Human-readable label.</param>
/// <param name="Value">The formatted value.</param>
public sealed record EntityProperty(string Group, string Name, string Value)
{
    /// <summary>The broker's raw key (e.g. <c>x-message-ttl</c>) when it differs from <see cref="Name"/>; shown as a tooltip.</summary>
    public string? Key { get; init; }
}
