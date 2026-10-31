using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using MessagingToolbox.Abstractions.Messaging;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusEntityPropertiesTests
{
    [Fact]
    public void ForQueue_Defaults_FormatsDurationsSizesAndFlags()
    {
        QueueProperties queue = ServiceBusModelFactory.QueueProperties(
            name: "orders",
            lockDuration: TimeSpan.FromSeconds(30),
            maxSizeInMegabytes: 1024,
            requiresDuplicateDetection: true,
            requiresSession: false,
            defaultMessageTimeToLive: TimeSpan.MaxValue,
            autoDeleteOnIdle: TimeSpan.MaxValue,
            deadLetteringOnMessageExpiration: false,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10),
            maxDeliveryCount: 10,
            enableBatchedOperations: true,
            status: EntityStatus.Active,
            forwardTo: "audit",
            forwardDeadLetteredMessagesTo: string.Empty,
            userMetadata: string.Empty,
            enablePartitioning: false);


        IReadOnlyList<EntityProperty> properties = AzureServiceBusEntityProperties.ForQueue(queue);


        properties.ShouldContain(new EntityProperty("Settings", "Lock duration", "30 s"));
        properties.ShouldContain(new EntityProperty("Settings", "Max delivery count", "10"));
        properties.ShouldContain(new EntityProperty("Settings", "Message TTL", "never"));
        properties.ShouldContain(new EntityProperty("Settings", "Max size", "1 GB"));
        properties.ShouldContain(new EntityProperty("Settings", "Duplicate detection", "yes, 10 min window"));
        properties.ShouldContain(new EntityProperty("Forwarding", "Forward to", "audit"));
        properties.ShouldNotContain(property => property.Name == "Forward dead-letters to" || property.Name == "User metadata");
    }

    [Fact]
    public void ForSubscription_Settings_IncludesFilterDeadLettering()
    {
        SubscriptionProperties subscription = ServiceBusModelFactory.SubscriptionProperties(
            topicName: "events",
            subscriptionName: "billing",
            lockDuration: TimeSpan.FromMinutes(1),
            requiresSession: true,
            defaultMessageTimeToLive: TimeSpan.FromDays(14),
            autoDeleteOnIdle: TimeSpan.MaxValue,
            deadLetteringOnMessageExpiration: true,
            maxDeliveryCount: 5,
            enableBatchedOperations: true,
            status: EntityStatus.Active,
            forwardTo: null,
            forwardDeadLetteredMessagesTo: null,
            userMetadata: "owned by billing");


        IReadOnlyList<EntityProperty> properties = AzureServiceBusEntityProperties.ForSubscription(subscription);


        properties.ShouldContain(new EntityProperty("Settings", "Message TTL", "14 d"));
        properties.ShouldContain(new EntityProperty("Settings", "Sessions", "yes"));
        properties.ShouldContain(property => property.Name == "Dead-letter on filter errors");
        properties.ShouldContain(new EntityProperty("Settings", "User metadata", "owned by billing"));
    }
}
