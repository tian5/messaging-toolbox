namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Receives messages destructively (receive-and-delete); callers must confirm first.</summary>
/// <param name="EntityPath">Queue name or "{topic}/subscriptions/{subscription}".</param>
/// <param name="SubQueue">Which sub-queue to receive from.</param>
/// <param name="MaxMessages">Maximum number of messages to receive.</param>
/// <param name="MaxWaitTime">Optional upper bound for the receive call.</param>
/// <param name="SessionId">
/// Receive from this session only (active sub-queue of a session-enabled entity). When null on a
/// session-enabled entity, the provider receives from whichever sessions are available.
/// </param>
public sealed record ReceiveRequest(
    string EntityPath,
    MessageSubQueue SubQueue,
    int MaxMessages,
    TimeSpan? MaxWaitTime,
    string? SessionId = null);
