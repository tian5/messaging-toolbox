namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// The heart of the design: every Blazor feature area and every
/// management command handler checks these flags before acting (ADR 5)
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>Create/update/delete entities (queues, topics, subscriptions, exchanges).</summary>
    public required bool CanManageEntities { get; init; }

    /// <summary>Manage routing rules: subscription filters and actions, or exchange bindings.</summary>
    public required bool CanManageRules { get; init; }

    /// <summary>Message inspection without consuming; see <see cref="MessagingEntity.PeekMode"/> for its side effects.</summary>
    public required bool CanPeek { get; init; }

    /// <summary>Settleable receive plus complete/abandon (and dead-letter/defer where supported).</summary>
    public required bool CanReceiveSettle { get; init; }

    /// <summary>Session-aware entities.</summary>
    public required bool SupportsSessions { get; init; }

    /// <summary>Scheduled enqueue times.</summary>
    public required bool SupportsScheduling { get; init; }

    /// <summary>Cross-entity transactional sends/settlements.</summary>
    public required bool SupportsTransactions { get; init; }

    /// <summary>
    /// Every message-bearing entity has a broker-managed dead-letter sub-queue (and a dead-letter
    /// settlement). Brokers that dead-letter into ordinary queues show those as entities instead.
    /// </summary>
    public required bool SupportsDeadLetterSubQueue { get; init; }

    /// <summary>
    /// Dead-lettering can carry a reason and description. Without it (RabbitMQ: a native reject
    /// records only <c>reason=rejected</c>), the UI asks for none.
    /// </summary>
    public required bool SupportsDeadLetterReason { get; init; }

    /// <summary>Defer as a settlement, and receiving deferred messages back by sequence number (ADR 13).</summary>
    public required bool SupportsDeferral { get; init; }

    /// <summary>
    /// Messages carry stable, broker-assigned sequence numbers: peek paging, the live-watch cursor,
    /// and operations addressed by sequence number (scan-and-settle, cancel-scheduled).
    /// </summary>
    public required bool SupportsSequenceNumbers { get; init; }

    /// <summary>Held messages have a lock that expires (and can be renewed); without it they stay held until settled.</summary>
    public required bool SupportsLockExpiry { get; init; }

    /// <summary>Broker-side message size ceiling (256 KB on the Azure Service Bus emulator, 100 MB on premium namespaces, …).</summary>
    public required long MaxMessageSizeBytes { get; init; }

    /// <summary>
    /// Why <see cref="CanManageEntities"/> is off when the provider could normally manage entities
    /// (e.g. the emulator admin endpoint didn't answer); <see langword="null"/> otherwise.
    /// </summary>
    public string? ManagementUnavailableReason { get; init; }
}
