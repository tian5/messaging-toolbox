using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// Creates Azure Service Bus providers (cloud and emulator) for the shared provider factory,
/// which caches them per connection.
/// </summary>
public sealed class AzureServiceBusProviderBuilder : IMessagingProviderBuilder
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IAzureServiceBusManagementFactory _managementFactory;

    public AzureServiceBusProviderBuilder(
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IAzureServiceBusManagementFactory managementFactory)
    {
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _managementFactory = managementFactory;
    }

    public ProviderKind Kind => ProviderKind.AzureServiceBus;

    public async Task<IMessagingProvider> CreateAsync(ConnectionInfo connection, CancellationToken cancellationToken) =>
        await AzureServiceBusProvider.CreateAsync(
            connection.ConnectionString,
            [new AspireModelDiscovery(connection.ModeledQueues, connection.ModeledTopics)],
            _loggerFactory.CreateLogger<AzureServiceBusProvider>(),
            _timeProvider,
            _managementFactory.Create(connection.ConnectionString, connection.AdminEndpoint),
            cancellationToken);
}
