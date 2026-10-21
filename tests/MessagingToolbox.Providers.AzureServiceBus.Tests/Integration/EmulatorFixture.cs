using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Integration;

/// <summary>
/// One Service Bus emulator (plus its SQL Server companion) per test class. Entities are fixed by
/// the emulator config, so every test works on entities of its own. Starts nothing unless the
/// emulator tests are enabled (see <see cref="EmulatorFactAttribute"/>); once enabled, a container
/// that fails to start fails the tests instead of letting them pass silently.
/// </summary>
public sealed class EmulatorFixture : IAsyncLifetime
{
    public const string PlainQueue = "rt-plain";
    public const string SettlementQueue = "rt-settlement";
    public const string SessionQueue = "rt-sessions";
    public const string DeferredQueue = "rt-deferred";
    public const string Topic = "rt-topic";
    public const string SubscriptionA = "rt-topic/subscriptions/rt-sub-a";
    public const string SubscriptionB = "rt-topic/subscriptions/rt-sub-b";
    public const string ResampleQueue = "repro-resample";
    public const string ConcurrentQueue = "repro-concurrent";
    public const string TreeQueue = "repro-tree";

    private const string SqlPassword = "P@ssw0rd12345!";
    private const string SasKeyName = "RootManageSharedAccessKey";
    private const string SasKey = "SAS_KEY_VALUE";
    private const int AmqpPort = 5672;
    private const int AdminPort = 5300;

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private INetwork? _network;
    private IContainer? _sqlContainer;
    private IContainer? _emulatorContainer;
    private string? _configDir;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The emulator's admin (management) endpoint as <c>host:port</c>.</summary>
    public string AdminEndpoint { get; private set; } = string.Empty;

    public static IReadOnlyList<string> Queues { get; } = [PlainQueue, SettlementQueue, SessionQueue, DeferredQueue, ResampleQueue, ConcurrentQueue, TreeQueue];

    public static IReadOnlyList<ModeledTopicInfo> Topics { get; } = [new(Topic, ["rt-sub-a", "rt-sub-b"])];

    public async Task InitializeAsync()
    {
        if (!EmulatorFactAttribute.IsEnabled)
        {
            return;
        }

        // Testcontainers maps a host directory onto the container path, so stage Config.json
        // in its own directory and mount that over the emulator's ConfigFiles.
        _configDir = Path.Combine(Path.GetTempPath(), $"messagingtoolbox-emulator-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_configDir);
        await File.WriteAllTextAsync(Path.Combine(_configDir, "Config.json"), EmulatorConfig);

        _network = new NetworkBuilder().Build();

#pragma warning disable CS0618 // Testcontainers 4.x still exposes the parameterless ContainerBuilder.
        _sqlContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithNetwork(_network)
            .WithNetworkAliases("sql")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .Build();

        _emulatorContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
            .WithNetwork(_network)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", "sql")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithResourceMapping(_configDir, "/ServiceBus_Emulator/ConfigFiles")
            .WithPortBinding(AmqpPort, true)
            .WithPortBinding(AdminPort, true)
            .Build();
#pragma warning restore CS0618

        await _network.CreateAsync();
        await _sqlContainer.StartAsync();
        await _emulatorContainer.StartAsync();

        ConnectionString =
            $"Endpoint=sb://localhost:{_emulatorContainer.GetMappedPublicPort(AmqpPort)};" +
            $"SharedAccessKeyName={SasKeyName};SharedAccessKey={SasKey};UseDevelopmentEmulator=true";
        AdminEndpoint = $"localhost:{_emulatorContainer.GetMappedPublicPort(AdminPort)}";

        await WaitUntilReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_emulatorContainer is not null)
        {
            await _emulatorContainer.DisposeAsync();
        }

        if (_sqlContainer is not null)
        {
            await _sqlContainer.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }

        if (_configDir is not null)
        {
            try
            {
                Directory.Delete(_configDir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    /// <summary>
    /// A provider wired like production: the emulator's admin endpoint for management, the
    /// Aspire-modeled entities as fallback. Pass <paramref name="management"/> to replace the admin plane.
    /// </summary>
    public AzureServiceBusProvider CreateProvider(IAzureServiceBusManagement? management = null)
        => new(
            ConnectionString,
            [new AspireModelDiscovery(Queues, Topics)],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            management ?? new AzureServiceBusManagementFactory().Create(ConnectionString, AdminEndpoint));

    // The container reports started before the emulator has loaded its config from SQL Server;
    // a peek succeeds only once the entities exist.
    private async Task WaitUntilReadyAsync()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        while (true)
        {
            try
            {
                await provider.PeekAsync(new PeekRequest(PlainQueue, MessageSubQueue.Active, 1, null), CancellationToken.None);
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static string EmulatorConfig => $$"""
        {
          "UserConfig": {
            "Namespaces": [
              {
                "Name": "sbemulatorns",
                "Queues": [
                  {{Queue(PlainQueue, requiresSession: false)}},
                  {{Queue(SettlementQueue, requiresSession: false)}},
                  {{Queue(SessionQueue, requiresSession: true)}},
                  {{Queue(DeferredQueue, requiresSession: false)}},
                  {{Queue(ResampleQueue, requiresSession: false)}},
                  {{Queue(ConcurrentQueue, requiresSession: false)}},
                  {{Queue(TreeQueue, requiresSession: false)}}
                ],
                "Topics": [
                  {
                    "Name": "{{Topic}}",
                    "Properties": { "DefaultMessageTimeToLive": "PT1H", "DuplicateDetectionHistoryTimeWindow": "PT20S", "RequiresDuplicateDetection": false },
                    "Subscriptions": [ {{Subscription("rt-sub-a")}}, {{Subscription("rt-sub-b")}} ]
                  }
                ]
              }
            ],
            "Logging": { "Type": "File" }
          }
        }
        """;

    private static string Queue(string name, bool requiresSession)
        => $$"""{ "Name": "{{name}}", "Properties": { "RequiresSession": {{(requiresSession ? "true" : "false")}}, "MaxDeliveryCount": 10, "LockDuration": "PT1M", "DeadLetteringOnMessageExpiration": false, "DefaultMessageTimeToLive": "PT1H", "DuplicateDetectionHistoryTimeWindow": "PT20S", "ForwardDeadLetteredMessagesTo": "", "ForwardTo": "", "RequiresDuplicateDetection": false } }""";

    private static string Subscription(string name)
        => $$"""{ "Name": "{{name}}", "Properties": { "DeadLetteringOnMessageExpiration": false, "DefaultMessageTimeToLive": "PT1H", "LockDuration": "PT1M", "MaxDeliveryCount": 10, "ForwardDeadLetteredMessagesTo": "", "ForwardTo": "", "RequiresSession": false } }""";
}
