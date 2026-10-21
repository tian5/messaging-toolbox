namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Receives deferred messages by sequence number with peek-lock, so they can be settled by lock token.</summary>
/// <param name="EntityPath">Queue name or "{topic}/subscriptions/{subscription}".</param>
/// <param name="SubQueue">Active or a dead-letter sub-queue; scheduled messages are never deferred.</param>
/// <param name="Messages">The deferred messages to receive.</param>
/// <param name="LockRenewalDuration">When set, the provider renews the locks in the background for this long.</param>
public sealed record ReceiveDeferredRequest(
    string EntityPath,
    MessageSubQueue SubQueue,
    IReadOnlyList<DeferredMessageReference> Messages,
    TimeSpan? LockRenewalDuration = null);
