using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>
/// A node in the explorer tree: connection → queues | topics → subscriptions, with dead-letter
/// sub-queues as child nodes. <see cref="SubQueue"/> is Active for entity nodes and
/// DeadLetter for their dead-letter children.
/// </summary>
public sealed record EntityTreeNodeDto
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required EntityKind Kind { get; init; }

    /// <summary>The broker's name for the entity's flavour (e.g. <c>stream</c>); display only.</summary>
    public string? TypeName { get; init; }

    public MessageSubQueue SubQueue { get; init; } = MessageSubQueue.Active;

    /// <summary>True for the virtual Active/Scheduled/DLQ child nodes under a message-bearing entity.</summary>
    public bool IsSubQueueNode { get; init; }

    public MessagingEntityStatus Status { get; init; } = MessagingEntityStatus.Unknown;

    /// <summary>The entity requires sessions (as far as the provider knows); set on entity and sub-queue nodes alike.</summary>
    public bool RequiresSession { get; init; }

    /// <summary>What a peek of this entity costs; set on entity and sub-queue nodes alike.</summary>
    public PeekMode PeekMode { get; init; } = PeekMode.NonDestructive;

    /// <summary>The exchange the entity dead-letters into (routing-based brokers); null when it has none.</summary>
    public string? DeadLetterExchange { get; init; }

    /// <summary>What this entity allows beyond the connection's capabilities; set on entity and sub-queue nodes alike.</summary>
    public EntityOperations Operations { get; init; } = EntityOperations.All;

    /// <summary>Broker-specific settings of the entity, formatted by the provider; display only.</summary>
    public IReadOnlyList<EntityProperty> Properties { get; init; } = [];

    public EntityRuntimeInfoDto? RuntimeInfo { get; init; }

    public IReadOnlyList<EntityTreeNodeDto> Children { get; init; } = [];
}
