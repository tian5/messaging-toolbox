using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class MessagingProviderFactoryTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAzureServiceBusManagement _management;
    private readonly IAzureServiceBusManagementFactory _managementFactory;
    private readonly MessagingProviderFactory _sut;

    public MessagingProviderFactoryTests()
    {
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _management = Substitute.For<IAzureServiceBusManagement>();
        _managementFactory = Substitute.For<IAzureServiceBusManagementFactory>();
        _managementFactory.Create(Arg.Any<string>(), Arg.Any<string?>()).Returns(_management);

        _connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId,
                Name = "messaging",
                DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus,
                ConnectionString = EmulatorConnectionString,
                Source = ConnectionSource.Config,
            });

        _sut = new MessagingProviderFactory(
            _connectionCatalog,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _managementFactory);
    }

    [Fact]
    public async Task GetProviderAsync_EmulatorProbeSucceeds_ReturnsProviderWithManagementCapabilities()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);


        IMessagingProvider provider = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
    }

    [Fact]
    public async Task GetProviderAsync_SecondCallForSameConnection_ReturnsSameProbedProviderWithoutReprobing()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);


        IMessagingProvider first = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);
        IMessagingProvider second = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        first.ShouldBeSameAs(second);
        first.Capabilities.CanManageEntities.ShouldBeTrue();
        await _management.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProviderAsync_EmulatorProbeFails_KeepsDataPlaneOnlyCapabilities()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));


        IMessagingProvider provider = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeFalse();
        provider.Capabilities.CanPeek.ShouldBeTrue();
    }

    [Fact]
    public async Task GetProviderAsync_FirstCreationFails_EvictsEntryAndRetriesOnNextCall()
    {
        _managementFactory.Create(Arg.Any<string>(), Arg.Any<string?>()).Throws(new InvalidOperationException("no broker"));


        await Should.ThrowAsync<InvalidOperationException>(() => _sut.GetProviderAsync(ConnectionId, CancellationToken.None));

        _managementFactory.Create(Arg.Any<string>(), Arg.Any<string?>()).Returns(_management);
        _management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        IMessagingProvider provider = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
    }

    [Fact]
    public async Task GetProviderAsync_FirstCallerCancelled_DoesNotPoisonCacheForNextCall()
    {
        TaskCompletionSource<bool> probeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(probeGate.Task);
        using CancellationTokenSource cancellation = new();

        Task<IMessagingProvider> first = _sut.GetProviderAsync(ConnectionId, cancellation.Token);


        // The caller gives up while creation is still probing; the in-flight creation is not
        // tied to that token and must finish for the next caller.
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        probeGate.SetResult(true);
        IMessagingProvider provider = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
        await _management.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }
}
