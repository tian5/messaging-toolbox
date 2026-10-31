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

    /// <summary>
    /// The broker's own name for the entity's flavour, for display only: RabbitMQ <c>classic</c>,
    /// <c>quorum</c> or <c>stream</c> queues and <c>direct</c>/<c>topic</c>/… exchanges. Null when
    /// the broker has a single flavour of <see cref="Kind"/>. Behaviour never depends on it — it
    /// comes from <see cref="PeekMode"/> and the provider capabilities.
    /// </summary>
    public string? TypeName { get; init; }

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

    /// <summary>
    /// The entity's messages carry stable, increasing sequence numbers for browsing — a peek from a
    /// position, the live-watch cursor — even where the connection's don't
    /// (<see cref="ProviderCapabilities.SupportsSequenceNumbers"/>; a RabbitMQ stream's offsets).
    /// It widens browsing only: operations addressed by sequence number still need the capability (ADR 24).
    /// </summary>
    public bool HasSequenceNumbers { get; init; }

    /// <summary>A peek can start at a point in time (<see cref="PeekRequest.FromTime"/>): RabbitMQ streams.</summary>
    public bool CanPeekFromTime { get; init; }

    /// <summary>
    /// The exchange this entity dead-letters into, for brokers that dead-letter through routing
    /// (RabbitMQ: the queue's or its policy's dead-letter exchange; empty = the default exchange).
    /// Null when the entity has none, or when the broker uses a dead-letter sub-queue instead.
    /// </summary>
    public string? DeadLetterExchange { get; init; }

    /// <summary>What this entity allows beyond the connection's capabilities; only ever narrows them.</summary>
    public EntityOperations Operations { get; init; } = EntityOperations.All;

    /// <summary>Broker-specific settings (TTL, max size, arguments, …), formatted for display.</summary>
    public IReadOnlyList<EntityProperty> Properties { get; init; } = [];

    /// <summary>Runtime counts/sizes when the provider can obtain them; null otherwise.</summary>
    public EntityRuntimeInfo? RuntimeInfo { get; init; }
}
