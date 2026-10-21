namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Identifies a deferred message for a receive by sequence number.</summary>
/// <param name="SequenceNumber">The broker sequence number, as reported by peek.</param>
/// <param name="SessionId">The message's session; required on a session-enabled entity's active sub-queue.</param>
public sealed record DeferredMessageReference(long SequenceNumber, string? SessionId = null);
