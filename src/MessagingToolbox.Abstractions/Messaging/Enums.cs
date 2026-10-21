namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Kind of a broker entity.</summary>
public enum EntityKind
{
    Queue,
    Topic,
    Subscription,
}

/// <summary>Sub-queue a message operation targets.</summary>
public enum MessageSubQueue
{
    Active,

    /// <summary>Scheduled (not yet active) messages; browsable by peek only, never receivable.</summary>
    Scheduled,
    DeadLetter,
    TransferDeadLetter,
}

/// <summary>Where a message stands in its entity, as reported by peek and receive.</summary>
public enum MessageState
{
    Active,

    /// <summary>Set aside by a defer settlement; only a receive by sequence number gets it back.</summary>
    Deferred,

    /// <summary>Not yet enqueued; becomes active at its scheduled enqueue time.</summary>
    Scheduled,
}

/// <summary>Settlement operation applied to a peek-locked message.</summary>
public enum SettlementOperation
{
    Complete,
    Abandon,
    Defer,
    DeadLetter,
}

/// <summary>
/// Identifies a broker provider implementation. One value per shipping provider; extended as
/// RabbitMQ/Kafka/NATS providers land (see the roadmap in the requirements doc).
/// </summary>
public enum ProviderKind
{
    AzureServiceBus,
}

/// <summary>Lifecycle status of a broker entity.</summary>
public enum MessagingEntityStatus
{
    Unknown,
    Active,
    SendDisabled,
    ReceiveDisabled,
    Disabled,
}
