using Aspire.Hosting.ApplicationModel;

namespace MessagingToolbox.Aspire.Hosting.Brokers;

/// <summary>Picks the broker adapter for a connection-string resource by its resource type.</summary>
internal static class MessagingToolboxBrokers
{
    private static readonly IMessagingToolboxBroker AzureServiceBus = new AzureServiceBusBroker();
    private static readonly IMessagingToolboxBroker RabbitMq = new RabbitMqBroker();

    // Plain connection-string resources (AddConnectionString) keep meaning Azure Service Bus, as
    // before RabbitMQ support; RabbitMQ is recognized by its server resource.
    public static IMessagingToolboxBroker For(IResource resource) => resource switch
    {
        RabbitMQServerResource => RabbitMq,
        _ => AzureServiceBus,
    };
}
