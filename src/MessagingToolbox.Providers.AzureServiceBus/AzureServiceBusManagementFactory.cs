using MessagingToolbox.Providers.AzureServiceBus.Emulator;

namespace MessagingToolbox.Providers.AzureServiceBus;

public sealed class AzureServiceBusManagementFactory : IAzureServiceBusManagementFactory
{
    public IAzureServiceBusManagement Create(string connectionString, string? adminEndpoint = null)
    {
        string managementConnectionString = EmulatorDetection.IsEmulatorConnectionString(connectionString)
            ? EmulatorAdminConnectionString.FromRuntimeConnectionString(connectionString, adminEndpoint) ?? connectionString
            : connectionString;

        return new AzureServiceBusManagement(managementConnectionString);
    }
}
