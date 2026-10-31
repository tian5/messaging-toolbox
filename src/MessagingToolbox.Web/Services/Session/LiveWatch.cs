using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// One pinned entity (queue, subscription or one of their sub-queues) the live watch peeks for
/// new arrivals. On active and scheduled views sequence numbers only grow, so a cursor past the
/// last seen one finds exactly the new messages. A dead-lettered message keeps its original
/// sequence number (verified on the emulator), so it can land below the cursor; dead-letter
/// views are walked whole and compared with the sequence numbers seen before. The collections are
/// replaced, never mutated, because the poll loop runs off the render thread.
/// </summary>
public sealed class LiveWatch
{
    /// <summary>Most recent arrivals kept per watch; older ones fall off.</summary>
    public const int MaxKept = 100;

    public required Guid ConnectionId { get; init; }

    public required string ConnectionName { get; init; }

    public required string Path { get; init; }

    public required MessageSubQueue SubQueue { get; init; }

    public string Key => $"{ConnectionId}|{Path}|{SubQueue}";

    /// <summary>
    /// False while the first walk skips what was already there; until then nothing counts as new.
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>Dead-letter views can't use a cursor: their messages keep the sequence number they had.</summary>
    public bool WalksWholeView => SubQueue is MessageSubQueue.DeadLetter or MessageSubQueue.TransferDeadLetter;

    /// <summary>
    /// Cursor views: highest sequence number seen so far; the next peek starts after it, or at the
    /// beginning while nothing was seen (a stream's first offset is 0).
    /// </summary>
    public long? LastSeenSequence { get; set; }

    /// <summary>Whole-view walks: the sequence numbers present at the last walk.</summary>
    public IReadOnlySet<long> SeenSequences { get; set; } = new HashSet<long>();

    /// <summary>True when the last walk hit the peek limit, so arrivals past it go unnoticed.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>Sequence numbers of the arrivals seen since the user's last send.</summary>
    public IReadOnlySet<long> SinceLastSend { get; set; } = new HashSet<long>();

    /// <summary>Arrivals since pinning (or the last Clear), newest first.</summary>
    public IReadOnlyList<MessageDto> Arrivals { get; set; } = [];

    /// <summary>Arrivals since pinning (or the last Clear), including those no longer kept.</summary>
    public int ArrivalCount { get; set; }

    public string? Error { get; set; }

    public bool IsSinceLastSend(MessageDto message)
        => message.SequenceNumber is { } sequence && SinceLastSend.Contains(sequence);

    public int SinceLastSendCount => SinceLastSend.Count;
}
