namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// A broker entity: queue, topic, subscription, or exchange (discriminated by <see cref="Kind"/>).
/// <see cref="Path"/> conventions: queues and topics use their name, subscriptions use
/// "{topic}/subscriptions/{subscription}" so the path is directly usable by provider clients.
/// </summary>
public sealed record MessagingEntity
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required EntityKind Kind { get; init; }

    /// <summary>Path of the owning topic for subscriptions; null for queues and topics.</summary>
    public string? ParentPath { get; init; }

    public MessagingEntityStatus Status { get; init; } = MessagingEntityStatus.Unknown;

    /// <summary>
    /// True when the entity is known to require sessions (queues and subscriptions); false when it
    /// doesn't or when the discovery channel can't tell (e.g. entities modeled in an Aspire AppHost).
    /// </summary>
    public bool RequiresSession { get; init; }

    /// <summary>What browsing this entity costs; providers without a read-only browse report <see cref="PeekMode.Requeue"/>.</summary>
    public PeekMode PeekMode { get; init; } = PeekMode.NonDestructive;

    /// <summary>Runtime counts/sizes when the provider can obtain them; null otherwise.</summary>
    public EntityRuntimeInfo? RuntimeInfo { get; init; }
}
