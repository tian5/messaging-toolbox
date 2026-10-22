using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// Capability profile of a RabbitMQ connection (ADR 5, ADR 17). Dead-lettering goes to ordinary
/// queues bound to a dead-letter exchange, so there is no dead-letter sub-queue; there are no
/// sequence numbers, sessions, deferral, scheduling or expiring locks.
/// </summary>
public static class RabbitMqCapabilities
{
    /// <summary>RabbitMQ 4's default <c>max_message_size</c>; the broker stays authoritative.</summary>
    public const long DefaultMaxMessageSizeBytes = 128L * 1024 * 1024;

    public static readonly ProviderCapabilities Default = new()
    {
        CanManageEntities = false,
        CanManageRules = false,
        CanPeek = true,
        CanReceiveSettle = false,
        SupportsSessions = false,
        SupportsScheduling = false,
        SupportsTransactions = false,
        SupportsDeadLetterSubQueue = false,
        SupportsDeferral = false,
        SupportsSequenceNumbers = false,
        SupportsLockExpiry = false,
        MaxMessageSizeBytes = DefaultMaxMessageSizeBytes,
    };
}
