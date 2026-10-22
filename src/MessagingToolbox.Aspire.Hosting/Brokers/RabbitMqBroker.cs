using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace MessagingToolbox.Aspire.Hosting.Brokers;

/// <summary>
/// RabbitMQ: the management plugin's endpoint, when the AppHost enabled it (<c>WithManagementPlugin()</c>).
/// Aspire doesn't model queues or exchanges, so discovery relies on that API; without it the toolbox
/// can only reach the data plane.
/// </summary>
internal sealed class RabbitMqBroker : IMessagingToolboxBroker
{
    // Endpoint name Aspire's WithManagementPlugin gives the management UI/API (container port 15672).
    private const string ManagementEndpointName = "management";

    public string Kind => "RabbitMQ";

    public void AddConnectionEnvironment(
        EnvironmentCallbackContext context,
        string prefix,
        IResource broker,
        IDistributedApplicationBuilder applicationBuilder)
    {
        // Aspire maps 15672 to a dynamic host port, so the toolbox can't derive it from the AMQP
        // connection string; the reference resolves for the consumer (host or container).
        if (broker is RabbitMQServerResource rabbitMq
            && rabbitMq.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.Name == ManagementEndpointName))
        {
            context.EnvironmentVariables[$"{prefix}__AdminEndpoint"] = new EndpointReference(rabbitMq, ManagementEndpointName);
        }
    }
}
