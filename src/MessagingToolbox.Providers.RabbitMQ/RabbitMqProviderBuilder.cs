using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// Creates RabbitMQ providers for the shared provider factory. The AMQP connection opens lazily on
/// first use, so a broker that is still starting doesn't fail the connection list.
/// </summary>
public sealed class RabbitMqProviderBuilder : IMessagingProviderBuilder
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IRabbitMqManagementClientFactory _managementFactory;

    public RabbitMqProviderBuilder(
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IRabbitMqManagementClientFactory managementFactory)
    {
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _managementFactory = managementFactory;
    }

    public ProviderKind Kind => ProviderKind.RabbitMQ;

    public async Task<IMessagingProvider> CreateAsync(ConnectionInfo connection, CancellationToken cancellationToken)
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse(connection.ConnectionString, connection.AdminEndpoint);

        RabbitMqProvider provider = new(
            settings,
            _managementFactory.Create(settings.ManagementUri, settings.VirtualHost, settings.UserName, settings.Password),
            connection.ModeledQueues,
            _loggerFactory.CreateLogger<RabbitMqProvider>(),
            _timeProvider);

        await provider.ProbeCapabilitiesAsync(cancellationToken);
        return provider;
    }
}
