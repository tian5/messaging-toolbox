namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>Runtime counters of an entity as displayed next to the tree and details view (FR-2.2).</summary>
/// <param name="IsApproximate">True when counts come from bounded peek sampling and may be capped.</param>
/// <param name="DeferredMessageCount">How many of the active messages are deferred; null when the broker doesn't report it.</param>
public sealed record EntityRuntimeInfoDto(
    long ActiveMessageCount,
    long ScheduledMessageCount,
    long DeadLetterMessageCount,
    long TransferDeadLetterMessageCount,
    long SizeInBytes,
    DateTimeOffset? UpdatedAt,
    bool IsApproximate = false,
    long? DeferredMessageCount = null);
