using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>A message as shown in the grid and detail view: body as text plus every property.</summary>
public sealed record MessageDto
{
    public required string Body { get; init; }

    /// <summary>Detected body format for display and export/import round-tripping.</summary>
    public required MessageBodyFormat BodyFormat { get; init; }

    public required int BodySizeBytes { get; init; }

    public string? MessageId { get; init; }

    public string? CorrelationId { get; init; }

    public string? SessionId { get; init; }

    public string? ReplyToSessionId { get; init; }

    public string? Subject { get; init; }

    public string? To { get; init; }

    public string? ReplyTo { get; init; }

    public string? PartitionKey { get; init; }

    public string? ContentType { get; init; }

    public TimeSpan? TimeToLive { get; init; }

    public DateTimeOffset? ScheduledEnqueueTime { get; init; }

    public string? RoutingKey { get; init; }

    public long? SequenceNumber { get; init; }

    /// <summary>Deferred messages are skipped by receives; they come back only by sequence number.</summary>
    public MessageState State { get; init; }

    public int DeliveryCount { get; init; }

    public DateTimeOffset? EnqueuedTime { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? DeadLetterReason { get; init; }

    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>Present when the message was received in peek-lock mode; required for settlement.</summary>
    public string? LockToken { get; init; }

    /// <summary>When the held peek-lock expires (UTC); populated only on peek-lock receives.</summary>
    public DateTimeOffset? LockedUntil { get; init; }

    public IReadOnlyDictionary<string, object?> ApplicationProperties { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>Read-only broker system properties (display only; never sent or imported).</summary>
    public IReadOnlyDictionary<string, object?> BrokerProperties { get; init; } =
        new Dictionary<string, object?>();
}
