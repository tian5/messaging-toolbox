namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// The normalized, broker-agnostic message model. Well-known system properties are typed;
/// everything broker-specific lives in <see cref="ApplicationProperties"/>. Settlement metadata
/// (sequence number, lock token, delivery count, dead-letter reason) is populated on peek/receive.
/// </summary>
public sealed record ToolboxMessage
{
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

    /// <summary>Routing key used when publishing to an exchange; reported on receive where the broker has one.</summary>
    public string? RoutingKey { get; init; }

    public required byte[] Body { get; init; }

    public IReadOnlyDictionary<string, object?> ApplicationProperties { get; init; } =
        new Dictionary<string, object?>();
    
    public long? SequenceNumber { get; init; }

    public MessageState State { get; init; }

    public int DeliveryCount { get; init; }

    public DateTimeOffset? EnqueuedTime { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? LockToken { get; init; }

    /// <summary>When the held peek-lock expires (UTC); populated only on peek-lock receives.</summary>
    public DateTimeOffset? LockedUntil { get; init; }

    public string? DeadLetterReason { get; init; }

    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>
    /// Path of the entity a dead-lettered message came from, when the broker records it (RabbitMQ:
    /// <c>x-first-death-queue</c>); the natural target for a resubmit.
    /// </summary>
    public string? DeadLetterSourcePath { get; init; }

    /// <summary>
    /// Read-only, broker-specific system properties for display (e.g. exchange, redelivered flag,
    /// death history). Never sent: a resubmit or clone starts from the typed properties.
    /// </summary>
    public IReadOnlyDictionary<string, object?> BrokerProperties { get; init; } =
        new Dictionary<string, object?>();
}
