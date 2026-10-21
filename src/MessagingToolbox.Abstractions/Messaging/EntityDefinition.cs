namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// Entity definition for management operations (Phase 2, FR-6.1/FR-6.2). The full settings
/// surface (lock duration, TTL, duplicate detection, forwarding, …) is filled in as the
/// management phase lands; the contract keeps room for it via <see cref="Options"/>.
/// </summary>
/// <param name="Path">Queue/topic name, or "{topic}/subscriptions/{subscription}" for subscriptions.</param>
/// <param name="Kind">Entity kind to create or update.</param>
/// <param name="ParentPath">Owning topic path for subscriptions; null otherwise.</param>
/// <param name="Options">Broker-specific settings bag keyed by well-known names.</param>
public sealed record EntityDefinition(
    string Path,
    EntityKind Kind,
    string? ParentPath,
    IReadOnlyDictionary<string, object?> Options);
