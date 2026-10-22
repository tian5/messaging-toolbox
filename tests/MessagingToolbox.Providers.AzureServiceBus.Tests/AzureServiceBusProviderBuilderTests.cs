using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusProviderBuilderTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private static readonly ConnectionInfo Connection = new()
    {
        Id = Guid.NewGuid(),
        Name = "messaging",
        DisplayName = "messaging",
        Kind = ProviderKind.AzureServiceBus,
        ConnectionString = EmulatorConnectionString,
        Source = ConnectionSource.Config,
        AdminEndpoint = "http://localhost:5300",
    };

    private readonly IAzureServiceBusManagement _management;
    private readonly IAzureServiceBusManagementFactory _managementFactory;
    private readonly AzureServiceBusProviderBuilder _sut;

    public AzureServiceBusProviderBuilderTests()
    {
        _management = Substitute.For<IAzureServiceBusManagement>();
        _managementFactory = Substitute.For<IAzureServiceBusManagementFactory>();
        _managementFactory.Create(Arg.Any<string>(), Arg.Any<string?>()).Returns(_management);

        _sut = new AzureServiceBusProviderBuilder(NullLoggerFactory.Instance, TimeProvider.System, _managementFactory);
    }

    [Fact]
    public void Kind_Always_IsAzureServiceBus()
    {
        _sut.Kind.ShouldBe(ProviderKind.AzureServiceBus);
    }

    [Fact]
    public async Task CreateAsync_EmulatorProbeSucceeds_ReturnsProviderWithManagementCapabilities()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);


        await using IMessagingProvider provider = await _sut.CreateAsync(Connection, CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_EmulatorProbeFails_KeepsDataPlaneOnlyCapabilities()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));


        await using IMessagingProvider provider = await _sut.CreateAsync(Connection, CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeFalse();
        provider.Capabilities.CanPeek.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_HostSuppliedAdminEndpoint_PassesItToManagementFactory()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);


        await using IMessagingProvider provider = await _sut.CreateAsync(Connection, CancellationToken.None);


        _managementFactory.Received(1).Create(EmulatorConnectionString, "http://localhost:5300");
    }
}
