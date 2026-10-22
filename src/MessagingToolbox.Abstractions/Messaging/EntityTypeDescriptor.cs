namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// An entity type a provider can create, with the options it accepts (ADR 21). The management UI
/// is built from these, so it never hard-codes a broker's settings.
/// </summary>
/// <param name="Kind">Entity kind created.</param>
/// <param name="Label">Human-readable name, lower case ("queue", "exchange", "subscription").</param>
/// <param name="Options">Settings, in display order.</param>
public sealed record EntityTypeDescriptor(EntityKind Kind, string Label, IReadOnlyList<EntityOptionDescriptor> Options)
{
    /// <summary>Kind of the entity this one is created under (subscriptions under topics); null for top-level entities.</summary>
    public EntityKind? ParentKind { get; init; }

    /// <summary>Help text for the whole type, e.g. which settings can change later.</summary>
    public string? Description { get; init; }
}
