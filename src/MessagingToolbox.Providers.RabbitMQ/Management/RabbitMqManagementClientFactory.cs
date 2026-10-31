namespace MessagingToolbox.Providers.RabbitMQ.Management;

public sealed class RabbitMqManagementClientFactory : IRabbitMqManagementClientFactory
{
    public IRabbitMqManagementClient Create(Uri managementUri, string virtualHost, string userName, string password)
        => new RabbitMqManagementClient(managementUri, virtualHost, userName, password);
}
