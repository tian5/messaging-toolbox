namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Runtime counters and timestamps of a broker entity.</summary>
public sealed record EntityRuntimeInfo
{
    public long ActiveMessageCount { get; init; }

    /// <summary>
    /// How many of the active messages are deferred; <see langword="null"/> when the broker doesn't
    /// report it (the management runtime properties don't — only sampled counts do).
    /// </summary>
    public long? DeferredMessageCount { get; init; }

    public long ScheduledMessageCount { get; init; }

    public long DeadLetterMessageCount { get; init; }

    public long TransferDeadLetterMessageCount { get; init; }

    public long SizeInBytes { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public DateTimeOffset? AccessedAt { get; init; }

    /// <summary>
    /// True when counts were sampled from bounded peeks (the only channel on connections without
    /// a management plane, e.g. the Azure Service Bus emulator) and may be capped.
    /// </summary>
    public bool IsApproximate { get; init; }
}
