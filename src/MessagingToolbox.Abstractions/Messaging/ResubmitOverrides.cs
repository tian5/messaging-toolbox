namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// Optional overrides applied when preparing a faithful clone of an existing message for resend or
/// repair. A <see langword="null"/> value means "preserve the original"; any provided value
/// replaces the corresponding property on the cloned message.
/// </summary>
public sealed record ResubmitOverrides(
    string? Body = null,
    string? ContentType = null,
    string? Subject = null,
    string? MessageId = null,
    string? CorrelationId = null,
    string? SessionId = null,
    string? ReplyToSessionId = null,
    string? To = null,
    string? ReplyTo = null,
    TimeSpan? TimeToLive = null,
    DateTimeOffset? ScheduledEnqueueTime = null,
    IReadOnlyDictionary<string, object?>? ApplicationProperties = null)
{
    /// <summary>An override set that preserves every property of the original message.</summary>
    public static ResubmitOverrides Empty { get; } = new();
}
