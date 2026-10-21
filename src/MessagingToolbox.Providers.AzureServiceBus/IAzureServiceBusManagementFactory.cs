namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// Creates the management-plane client for a connection. The emulator swaps to the derived
/// admin endpoint (architecture doc §6) — the host-resolved <c>adminEndpoint</c> when the host resolved
/// it, else <c>:5300</c> on the data-plane host; cloud namespaces use the connection string as-is.
/// </summary>
public interface IAzureServiceBusManagementFactory
{
    IAzureServiceBusManagement Create(string connectionString, string? adminEndpoint = null);
}
