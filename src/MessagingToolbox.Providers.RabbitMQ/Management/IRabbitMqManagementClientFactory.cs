namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>Creates the management client for one connection (test seam, like the Azure Service Bus management factory).</summary>
public interface IRabbitMqManagementClientFactory
{
    IRabbitMqManagementClient Create(Uri managementUri, string virtualHost, string userName, string password);
}
