namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Non-destructive inspection of messages (FR-3.1).</summary>
/// <param name="EntityPath">Queue name, topic name, or "{topic}/subscriptions/{subscription}".</param>
/// <param name="SubQueue">Which sub-queue to peek.</param>
/// <param name="MaxMessages">How many messages to fetch; providers page internally and clamp the total (NFR-2).</param>
/// <param name="FromSequenceNumber">Optional sequence number to start peeking from.</param>
/// <param name="SessionId">
/// Optional session filter: only messages of this session are returned. Applied while paging, so
/// browsing stays side-effect free (no session is locked).
/// </param>
public sealed record PeekRequest(
    string EntityPath,
    MessageSubQueue SubQueue,
    int MaxMessages,
    long? FromSequenceNumber,
    string? SessionId = null);
