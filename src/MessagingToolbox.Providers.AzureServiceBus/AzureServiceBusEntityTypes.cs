using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// What the management UI can create on Service Bus (ADR 21). Keys and value types are the ones
/// <see cref="AzureServiceBusManagement"/> applies, so the schema and the provider can't drift apart.
/// </summary>
internal static class AzureServiceBusEntityTypes
{
    private const string DurationPlaceholder = "hh:mm:ss";

    private static readonly EntityOptionDescriptor LockDuration = new("LockDuration", "Lock duration", EntityOptionType.Duration)
    {
        Placeholder = DurationPlaceholder,
        Description = "How long a received message stays locked before another receiver can get it.",
    };

    private static readonly EntityOptionDescriptor MessageTimeToLive = new("DefaultMessageTimeToLive", "Default message TTL", EntityOptionType.Duration)
    {
        Placeholder = DurationPlaceholder,
    };

    private static readonly EntityOptionDescriptor MaxSize = new("MaxSizeInMegabytes", "Max size (MB)", EntityOptionType.Integer);

    private static readonly EntityOptionDescriptor RequiresSession = new("RequiresSession", "Requires session", EntityOptionType.Boolean)
    {
        DefaultValue = false,
        CreateOnly = true,
    };

    private static readonly EntityOptionDescriptor DeadLetterOnExpiration = new("EnableDeadLetteringOnMessageExpiration", "Dead-letter on expiration", EntityOptionType.Boolean)
    {
        DefaultValue = false,
    };

    private static readonly EntityOptionDescriptor ForwardTo = new("ForwardTo", "Forward to", EntityOptionType.Text)
    {
        Placeholder = "queue or topic name",
    };

    private static readonly EntityOptionDescriptor AutoDeleteOnIdle = new("AutoDeleteOnIdle", "Auto-delete on idle", EntityOptionType.Duration)
    {
        Placeholder = DurationPlaceholder,
    };

    private static readonly EntityOptionDescriptor EnablePartitioning = new("EnablePartitioning", "Enable partitioning", EntityOptionType.Boolean)
    {
        DefaultValue = false,
        CreateOnly = true,
    };

    private static readonly EntityOptionDescriptor DuplicateDetectionWindow = new("DuplicateDetectionHistoryTimeWindow", "Duplicate detection window", EntityOptionType.Duration)
    {
        Placeholder = DurationPlaceholder,
    };

    public static readonly IReadOnlyList<EntityTypeDescriptor> All =
    [
        new(EntityKind.Queue, "queue",
        [
            LockDuration, MessageTimeToLive, MaxSize, RequiresSession, DeadLetterOnExpiration,
            ForwardTo, AutoDeleteOnIdle, EnablePartitioning, DuplicateDetectionWindow,
        ]),
        new(EntityKind.Topic, "topic",
        [
            MessageTimeToLive, MaxSize, AutoDeleteOnIdle, EnablePartitioning, DuplicateDetectionWindow,
        ]),
        new(EntityKind.Subscription, "subscription",
        [
            LockDuration, MessageTimeToLive, RequiresSession, DeadLetterOnExpiration, ForwardTo, AutoDeleteOnIdle,
        ])
        {
            ParentKind = EntityKind.Topic,
        },
    ];
}
