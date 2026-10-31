namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Kind of a broker entity.</summary>
public enum EntityKind
{
    Queue,
    Topic,
    Subscription,

    /// <summary>A routing entity that holds no messages itself (e.g. a RabbitMQ exchange).</summary>
    Exchange,
}

/// <summary>What browsing (peek) an entity costs.</summary>
public enum PeekMode
{
    /// <summary>Peek is free: no lock, no delivery, no state change.</summary>
    NonDestructive,

    /// <summary>
    /// The broker has no read-only browse: peek takes the messages and requeues them, so they are
    /// marked redelivered, briefly invisible to consumers, and may change order. Only ever on an
    /// explicit Load — never from counts, sampling or live watch.
    /// </summary>
    Requeue,
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
/// Kafka/NATS providers land (see the roadmap in the requirements doc). Persisted by name.
/// </summary>
public enum ProviderKind
{
    AzureServiceBus,
    RabbitMQ,
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

/// <summary>How an entity option is edited and which CLR type carries its value in <see cref="EntityDefinition.Options"/>.</summary>
public enum EntityOptionType
{
    /// <summary><see cref="string"/>.</summary>
    Text,

    /// <summary><see cref="bool"/>.</summary>
    Boolean,

    /// <summary><see cref="long"/>.</summary>
    Integer,

    /// <summary><see cref="System.TimeSpan"/>.</summary>
    Duration,

    /// <summary><see cref="string"/>, one of <see cref="EntityOptionDescriptor.Choices"/>.</summary>
    Choice,
}
