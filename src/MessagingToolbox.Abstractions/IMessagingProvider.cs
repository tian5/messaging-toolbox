using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Abstractions;

/// <summary>
/// The provider contract every broker plugs into. Implementations translate SDK exceptions
/// into the typed errors in <see cref="Errors"/> and honor <see cref="Capabilities"/>.
/// </summary>
public interface IMessagingProvider : IAsyncDisposable
{
    ProviderKind Kind { get; }

    ProviderCapabilities Capabilities { get; }

    IAsyncEnumerable<MessagingEntity> ListEntitiesAsync(CancellationToken cancellationToken);

    Task<EntityRuntimeInfo> GetRuntimeInfoAsync(string entityPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<ToolboxMessage>> PeekAsync(PeekRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ToolboxMessage>> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken);

    Task SendAsync(string entityPath, IReadOnlyList<ToolboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Receives messages with peek-lock so they can be settled later via <see cref="SettleAsync"/>
    /// (by lock token). The messages remain in the queue; their locks are held for at most the
    /// entity's lock duration unless <paramref name="lockRenewalDuration"/> is set, in which case
    /// the provider renews the locks in the background until that duration elapses.
    /// Non-destructive browsing should use <see cref="PeekAsync"/> instead.
    /// On a session-enabled entity the provider locks the sessions it receives from (only
    /// <paramref name="sessionId"/> when set) and holds each session until all of its received
    /// messages are settled or the session lock expires; meanwhile no other receiver gets that session.
    /// </summary>
    Task<IReadOnlyList<ToolboxMessage>> ReceiveForSettlementAsync(
        string entityPath,
        MessageSubQueue subQueue,
        int maxMessages,
        TimeSpan? maxWaitTime,
        CancellationToken cancellationToken,
        TimeSpan? lockRenewalDuration = null,
        string? sessionId = null);

    /// <summary>
    /// Receives deferred messages by sequence number with peek-lock, tracked like
    /// <see cref="ReceiveForSettlementAsync"/> so they can be settled via <see cref="SettleAsync"/>.
    /// Deferred messages are skipped by every other receive; this is the only way to get them back.
    /// A message that is not deferred, is locked by another receive, or no longer exists is skipped
    /// and missing from the result. Each receive counts as a delivery. On a session-enabled entity's
    /// active sub-queue each message is received through its own session, which stays locked until
    /// its received messages are settled, as with <see cref="ReceiveForSettlementAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ToolboxMessage>> ReceiveDeferredForSettlementAsync(ReceiveDeferredRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reports the current expiry (UTC) of held peek-locks, keyed by lock token — for tokens the
    /// provider still holds. Renewals move the expiry forward, so polling this refreshes the
    /// lock countdown in the UI without re-receiving the messages.
    /// </summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetHeldLockStatesAsync(
        string entityPath,
        MessageSubQueue subQueue,
        IReadOnlyList<string> lockTokens,
        CancellationToken cancellationToken);

    /// <summary>
    /// Settles exactly the messages identified by their lock tokens (obtained from
    /// <see cref="ReceiveForSettlementAsync"/>). A token that is unknown or whose lock has
    /// expired (e.g. the lock duration elapsed) is skipped and reported as not settled;
    /// any other broker failure aborts the operation.
    /// </summary>
    /// <returns>The lock tokens that were actually settled.</returns>
    Task<IReadOnlyList<string>> SettleAsync(SettleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Settles messages on a dead-letter sub-queue (DeadLetter or TransferDeadLetter) identified
    /// by broker sequence number. The provider walks the sub-queue with a bounded peek-lock
    /// receive, holding the locks of everything it passes (locked messages are skipped by later
    /// receives, which is what lets the walk reach the tail in one pass), settles the matching
    /// sequence numbers immediately, and releases the remaining locks by abandoning them one by
    /// one before the receiver closes — non-matching messages return to the sub-queue. Every walk delivery increments the
    /// message's delivery count — harmless on a dead-letter sub-queue, where delivery count is
    /// terminal, but destructive elsewhere — so implementations must throw
    /// <see cref="Errors.NotSupportedByProviderException"/> for any other sub-queue.
    /// </summary>
    /// <returns>The sequence numbers that were actually settled.</returns>
    Task<IReadOnlyList<long>> SettleBySequenceNumbersAsync(SettleBySequenceRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Purges a sub-queue via batched receive-and-delete; returns the number of messages deleted.
    /// On a session-enabled entity every available session is drained in turn; sessions locked by
    /// another receiver are skipped. Deferred messages, which a plain drain never sees, are then
    /// received and deleted by sequence number; ones locked by another receive are skipped.
    /// </summary>
    Task<long> PurgeAsync(string entityPath, MessageSubQueue subQueue, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels scheduled messages by sequence number — the only way to remove a message that is not
    /// yet enqueued; returns the sequence numbers actually cancelled.
    /// </summary>
    Task<IReadOnlyList<long>> CancelScheduledAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// The entity types this connection can create and the options each accepts (ADR 21); empty
    /// when it can't manage entities at all.
    /// </summary>
    IReadOnlyList<EntityTypeDescriptor> GetEntityTypes();

    Task<MessagingEntity> CreateOrUpdateEntityAsync(EntityDefinition definition, CancellationToken cancellationToken);

    Task DeleteEntityAsync(string entityPath, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the management definition of an existing entity so it can be edited. Throws
    /// <see cref="Errors.MessagingEntityNotFoundException"/> when the entity does not exist.
    /// </summary>
    Task<EntityDefinition> GetEntityDefinitionAsync(string entityPath, CancellationToken cancellationToken);

    /// <summary>
    /// Prepares a faithful clone of <paramref name="original"/> for resend or repair. The provider
    /// preserves as much of the original broker message as possible, applies the requested
    /// overrides, assigns a fresh message identity when none is supplied, and marks the clone with
    /// a traceability property pointing back to the original.
    /// </summary>
    Task<ToolboxMessage> PrepareResubmitAsync(
        string sourceEntityPath,
        MessageSubQueue sourceSubQueue,
        ToolboxMessage original,
        ResubmitOverrides overrides,
        CancellationToken cancellationToken);
}
