namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// Entity definition for management operations (FR-6.1/FR-6.2). It names the entity rather than
/// carrying its path: the provider builds the path, so callers never need a broker's path
/// conventions (ADR 21).
/// </summary>
/// <param name="Name">The entity's own name (a subscription's name without its topic).</param>
/// <param name="Kind">Entity kind to create or update.</param>
/// <param name="ParentPath">Path of the parent entity when the type has one (a subscription's topic); null otherwise.</param>
/// <param name="Options">Settings keyed and typed as the provider's <see cref="EntityTypeDescriptor"/> describes.</param>
public sealed record EntityDefinition(
    string Name,
    EntityKind Kind,
    string? ParentPath,
    IReadOnlyDictionary<string, object?> Options);
