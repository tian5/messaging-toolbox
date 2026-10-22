using Azure.Messaging.ServiceBus.Administration;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>Turns administration-client properties into display-only <see cref="EntityProperty"/> lists.</summary>
internal static class AzureServiceBusEntityProperties
{
    private const string Settings = "Settings";
    private const string Forwarding = "Forwarding";

    public static IReadOnlyList<EntityProperty> ForQueue(QueueProperties queue)
    {
        List<EntityProperty> properties =
        [
            Setting("Lock duration", EntityPropertyFormat.Duration(queue.LockDuration)),
            Setting("Max delivery count", EntityPropertyFormat.Count(queue.MaxDeliveryCount)),
            Setting("Message TTL", EntityPropertyFormat.Duration(queue.DefaultMessageTimeToLive)),
            Setting("Auto-delete on idle", EntityPropertyFormat.Duration(queue.AutoDeleteOnIdle)),
            Setting("Max size", EntityPropertyFormat.Bytes(queue.MaxSizeInMegabytes * 1024 * 1024)),
            Setting("Sessions", EntityPropertyFormat.Flag(queue.RequiresSession)),
            Setting("Duplicate detection", DuplicateDetection(queue.RequiresDuplicateDetection, queue.DuplicateDetectionHistoryTimeWindow)),
            Setting("Dead-letter on expiration", EntityPropertyFormat.Flag(queue.DeadLetteringOnMessageExpiration)),
            Setting("Partitioned", EntityPropertyFormat.Flag(queue.EnablePartitioning)),
            Setting("Batched operations", EntityPropertyFormat.Flag(queue.EnableBatchedOperations)),
        ];
        AddMaxMessageSize(properties, queue.MaxMessageSizeInKilobytes);
        AddCommon(properties, queue.ForwardTo, queue.ForwardDeadLetteredMessagesTo, queue.UserMetadata);
        return properties;
    }

    public static IReadOnlyList<EntityProperty> ForTopic(TopicProperties topic)
    {
        List<EntityProperty> properties =
        [
            Setting("Message TTL", EntityPropertyFormat.Duration(topic.DefaultMessageTimeToLive)),
            Setting("Auto-delete on idle", EntityPropertyFormat.Duration(topic.AutoDeleteOnIdle)),
            Setting("Max size", EntityPropertyFormat.Bytes(topic.MaxSizeInMegabytes * 1024 * 1024)),
            Setting("Duplicate detection", DuplicateDetection(topic.RequiresDuplicateDetection, topic.DuplicateDetectionHistoryTimeWindow)),
            Setting("Partitioned", EntityPropertyFormat.Flag(topic.EnablePartitioning)),
            Setting("Support ordering", EntityPropertyFormat.Flag(topic.SupportOrdering)),
            Setting("Batched operations", EntityPropertyFormat.Flag(topic.EnableBatchedOperations)),
        ];
        AddMaxMessageSize(properties, topic.MaxMessageSizeInKilobytes);
        AddCommon(properties, forwardTo: null, forwardDeadLetteredMessagesTo: null, topic.UserMetadata);
        return properties;
    }

    public static IReadOnlyList<EntityProperty> ForSubscription(SubscriptionProperties subscription)
    {
        List<EntityProperty> properties =
        [
            Setting("Lock duration", EntityPropertyFormat.Duration(subscription.LockDuration)),
            Setting("Max delivery count", EntityPropertyFormat.Count(subscription.MaxDeliveryCount)),
            Setting("Message TTL", EntityPropertyFormat.Duration(subscription.DefaultMessageTimeToLive)),
            Setting("Auto-delete on idle", EntityPropertyFormat.Duration(subscription.AutoDeleteOnIdle)),
            Setting("Sessions", EntityPropertyFormat.Flag(subscription.RequiresSession)),
            Setting("Dead-letter on expiration", EntityPropertyFormat.Flag(subscription.DeadLetteringOnMessageExpiration)),
            Setting("Dead-letter on filter errors", EntityPropertyFormat.Flag(subscription.EnableDeadLetteringOnFilterEvaluationExceptions)),
            Setting("Batched operations", EntityPropertyFormat.Flag(subscription.EnableBatchedOperations)),
        ];
        AddCommon(properties, subscription.ForwardTo, subscription.ForwardDeadLetteredMessagesTo, subscription.UserMetadata);
        return properties;
    }

    private static EntityProperty Setting(string name, string value) => new(Settings, name, value);

    private static string DuplicateDetection(bool enabled, TimeSpan window)
        => enabled ? $"yes, {EntityPropertyFormat.Duration(window)} window" : EntityPropertyFormat.Flag(false);

    // Only premium namespaces configure it; elsewhere the client reports null.
    private static void AddMaxMessageSize(List<EntityProperty> properties, long? kilobytes)
    {
        if (kilobytes is > 0)
        {
            properties.Add(Setting("Max message size", EntityPropertyFormat.Bytes(kilobytes.Value * 1024)));
        }
    }

    private static void AddCommon(List<EntityProperty> properties, string? forwardTo, string? forwardDeadLetteredMessagesTo, string? userMetadata)
    {
        if (!string.IsNullOrEmpty(forwardTo))
        {
            properties.Add(new EntityProperty(Forwarding, "Forward to", forwardTo));
        }

        if (!string.IsNullOrEmpty(forwardDeadLetteredMessagesTo))
        {
            properties.Add(new EntityProperty(Forwarding, "Forward dead-letters to", forwardDeadLetteredMessagesTo));
        }

        if (!string.IsNullOrEmpty(userMetadata))
        {
            properties.Add(Setting("User metadata", userMetadata));
        }
    }
}
