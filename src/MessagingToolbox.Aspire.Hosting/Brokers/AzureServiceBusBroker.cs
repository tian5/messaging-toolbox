using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace MessagingToolbox.Aspire.Hosting.Brokers;

/// <summary>Azure Service Bus: the emulator's admin endpoint plus the modeled queues, topics and subscriptions.</summary>
internal sealed class AzureServiceBusBroker : IMessagingToolboxBroker
{
    // Endpoint name Aspire's RunAsEmulator gives the emulator's port 5300 (health + admin API).
    private const string EmulatorAdminEndpointName = "emulatorhealth";

    public string Kind => "AzureServiceBus";

    public void AddConnectionEnvironment(
        EnvironmentCallbackContext context,
        string prefix,
        IResource broker,
        IDistributedApplicationBuilder applicationBuilder)
    {
        // The emulator's admin API listens on container port 5300, which Aspire maps to a
        // dynamic host port — the toolbox can't derive it from the connection string when it
        // runs from source. The endpoint reference resolves for the consumer (host or container).
        if (broker is AzureServiceBusResource { IsEmulator: true } emulator)
        {
            context.EnvironmentVariables[$"{prefix}__AdminEndpoint"] = new EndpointReference(emulator, EmulatorAdminEndpointName);
        }

        int queueIndex = 0;
        foreach (AzureServiceBusQueueResource queue in applicationBuilder.Resources.OfType<AzureServiceBusQueueResource>()
            .Where(queue => ReferenceEquals(queue.Parent, broker)))
        {
            context.EnvironmentVariables[$"{prefix}__Queues__{queueIndex++}"] = queue.QueueName;
        }

        int topicIndex = 0;
        foreach (AzureServiceBusTopicResource topic in applicationBuilder.Resources.OfType<AzureServiceBusTopicResource>()
            .Where(topic => ReferenceEquals(topic.Parent, broker)))
        {
            string topicPrefix = $"{prefix}__Topics__{topicIndex++}";
            context.EnvironmentVariables[$"{topicPrefix}__Name"] = topic.TopicName;

            int subscriptionIndex = 0;
            foreach (AzureServiceBusSubscriptionResource subscription in applicationBuilder.Resources.OfType<AzureServiceBusSubscriptionResource>()
                .Where(subscription => ReferenceEquals(subscription.Parent, topic)))
            {
                context.EnvironmentVariables[$"{topicPrefix}__Subscriptions__{subscriptionIndex++}"] = subscription.SubscriptionName;
            }
        }
    }
}
