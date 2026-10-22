using MessagingToolbox.Providers.RabbitMQ.Management;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests.Integration;

/// <summary>
/// One RabbitMQ broker (with the management plugin) per test class. Tests declare their own
/// queues through <see cref="DeclareAsync"/>, so they never share state. Starts nothing unless the
/// broker tests are enabled (see <see cref="BrokerFactAttribute"/>).
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private const string Image = "rabbitmq:4.1-management";
    private const int ManagementPort = 15672;

    private RabbitMqContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public string ManagementEndpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!BrokerFactAttribute.IsEnabled)
        {
            return;
        }

        _container = new RabbitMqBuilder(Image)
            .WithPortBinding(ManagementPort, assignRandomHostPort: true)
            .Build();
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        ManagementEndpoint = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ManagementPort)}";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public RabbitMqProvider CreateProvider(IReadOnlyList<string>? configuredQueues = null, string? managementEndpoint = null)
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse(ConnectionString, managementEndpoint ?? ManagementEndpoint);
        return new RabbitMqProvider(
            settings,
            new RabbitMqManagementClient(settings.ManagementUri, settings.VirtualHost, settings.UserName, settings.Password),
            configuredQueues ?? [],
            NullLogger<RabbitMqProvider>.Instance,
            TimeProvider.System);
    }

    /// <summary>Runs topology setup (declares, binds, seed publishes) on a channel of its own.</summary>
    public async Task DeclareAsync(Func<IChannel, Task> declare)
    {
        ConnectionFactory factory = new() { Uri = new Uri(ConnectionString) };
        await using IConnection connection = await factory.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        await declare(channel);
    }
}
