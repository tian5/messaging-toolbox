using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusProviderTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private const string CloudConnectionString =
        "Endpoint=sb://namespace.servicebus.windows.net;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=ZmFrZQ==";

    [Fact]
    public async Task Capabilities_EmulatorConnection_HidesManagementPlaneBeforeProbe()
    {
        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString);

        provider.Kind.ShouldBe(ProviderKind.AzureServiceBus);
        provider.Capabilities.CanManageEntities.ShouldBeFalse();
        provider.Capabilities.CanManageRules.ShouldBeFalse();
        provider.Capabilities.CanPeek.ShouldBeTrue();
        provider.Capabilities.MaxMessageSizeBytes.ShouldBe(256 * 1024);
    }

    [Fact]
    public async Task Capabilities_CloudConnection_ExposesManagementPlane()
    {
        await using AzureServiceBusProvider provider = CreateProvider(CloudConnectionString);

        provider.Capabilities.CanManageEntities.ShouldBeTrue();
        provider.Capabilities.CanManageRules.ShouldBeTrue();
        provider.Capabilities.MaxMessageSizeBytes.ShouldBe(100 * 1024 * 1024);
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_EmulatorProbeSucceeds_FlipsCanManageEntities()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_EmulatorProbeFails_KeepsCanManageEntitiesFalse()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeFalse();
    }

    [Fact]
    public async Task GetRuntimeInfoAsync_Emulator_ReturnsSampledCounts()
    {
        EntityRuntimeInfo sampled = new()
        {
            ActiveMessageCount = 5,
            ScheduledMessageCount = 2,
            DeadLetterMessageCount = 1,
            IsApproximate = true,
        };

        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync("orders", Arg.Any<CancellationToken>()).Returns(sampled);

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, sampler: sampler);


        EntityRuntimeInfo info = await provider.GetRuntimeInfoAsync("orders", CancellationToken.None);


        info.ActiveMessageCount.ShouldBe(5);
        info.ScheduledMessageCount.ShouldBe(2);
        info.DeadLetterMessageCount.ShouldBe(1);
        info.IsApproximate.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateOrUpdateEntityAsync_Emulator_WhenNotCapable_ThrowsNotSupported()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);
        EntityDefinition definition = new("orders", EntityKind.Queue, null, new Dictionary<string, object?>());

        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => provider.CreateOrUpdateEntityAsync(definition, CancellationToken.None));
    }

    [Fact]
    public async Task CreateOrUpdateEntityAsync_Emulator_WhenCapable_RoutesToManagement()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        management.CreateOrUpdateQueueAsync("orders", Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(new MessagingEntity { Path = "orders", Name = "orders", Kind = EntityKind.Queue });

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);
        EntityDefinition definition = new("orders", EntityKind.Queue, null, new Dictionary<string, object?> { ["RequiresSession"] = true });


        MessagingEntity entity = await provider.CreateOrUpdateEntityAsync(definition, CancellationToken.None);


        entity.Path.ShouldBe("orders");
        await management.Received(1).CreateOrUpdateQueueAsync(
            "orders",
            Arg.Is<IReadOnlyDictionary<string, object?>>(options => (bool?)options["RequiresSession"] == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteEntityAsync_Emulator_WhenCapable_RoutesToManagement()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        await provider.DeleteEntityAsync("orders", CancellationToken.None);


        await management.Received(1).DeleteQueueAsync("orders", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEntityDefinitionAsync_Emulator_WhenNotCapable_ThrowsNotSupported()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => provider.GetEntityDefinitionAsync("orders", CancellationToken.None));
    }

    [Fact]
    public async Task GetEntityDefinitionAsync_Emulator_WhenCapable_RoutesToManagement()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        management.GetQueueDefinitionAsync("orders", Arg.Any<CancellationToken>())
            .Returns(new EntityDefinition("orders", EntityKind.Queue, null, new Dictionary<string, object?>()));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        EntityDefinition definition = await provider.GetEntityDefinitionAsync("orders", CancellationToken.None);


        definition.Kind.ShouldBe(EntityKind.Queue);
        await management.Received(1).GetQueueDefinitionAsync("orders", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEntityDefinitionAsync_Subscription_RoutesToSubscriptionManagement()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        management.GetSubscriptionDefinitionAsync("events", "processor", Arg.Any<CancellationToken>())
            .Returns(new EntityDefinition("events/subscriptions/processor", EntityKind.Subscription, "events", new Dictionary<string, object?>()));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        EntityDefinition definition = await provider.GetEntityDefinitionAsync("events/subscriptions/processor", CancellationToken.None);


        definition.Kind.ShouldBe(EntityKind.Subscription);
        definition.ParentPath.ShouldBe("events");
        await management.Received(1).GetSubscriptionDefinitionAsync("events", "processor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListEntitiesAsync_Emulator_WhenCapable_UsesAdminListing()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        management.ListEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns([
                new MessagingEntity { Path = "orders", Name = "orders", Kind = EntityKind.Queue }
            ]);

        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("no broker in unit tests"));

        await using AzureServiceBusProvider provider = CreateProvider(
            EmulatorConnectionString,
            [new AspireModelDiscovery(["aspire-queue"], [])],
            sampler,
            management);


        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }


        entities.Count.ShouldBe(1);
        entities.ShouldContain(entity => entity.Kind == EntityKind.Queue && entity.Path == "orders");
    }

    [Fact]
    public async Task ReceiveAsync_ScheduledSubQueue_ThrowsNotSupported()
    {
        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString);
        ReceiveRequest request = new("orders", MessageSubQueue.Scheduled, 10, null);


        NotSupportedByProviderException exception = await Should.ThrowAsync<NotSupportedByProviderException>(
            () => provider.ReceiveAsync(request, CancellationToken.None));


        exception.Message.ShouldContain("scheduled");
    }

    [Fact]
    public async Task PurgeAsync_ScheduledSubQueue_ThrowsNotSupported()
    {
        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString);

        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => provider.PurgeAsync("orders", MessageSubQueue.Scheduled, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_OversizedMessage_ThrowsMessagingProviderException()
    {
        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString);
        ToolboxMessage oversized = new() { Body = new byte[256 * 1024 + 1] };


        MessagingProviderException exception = await Should.ThrowAsync<MessagingProviderException>(
            () => provider.SendAsync("orders", [oversized], CancellationToken.None));


        exception.Message.ShouldContain("exceeds the maximum");
    }

    [Fact]
    public async Task ListEntitiesAsync_Emulator_UsesDiscoveryChain()
    {
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("no broker in unit tests"));

        await using AzureServiceBusProvider provider = CreateProvider(
            EmulatorConnectionString,
            [new AspireModelDiscovery(["orders"], [new ModeledTopicInfo("billing-events", ["processor"])])],
            sampler);


        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }


        entities.Count.ShouldBe(3);
        entities.ShouldContain(entity => entity.Kind == EntityKind.Queue && entity.Path == "orders");
        entities.ShouldContain(entity => entity.Kind == EntityKind.Subscription && entity.Path == "billing-events/subscriptions/processor");
    }

    [Fact]
    public async Task ListEntitiesAsync_Emulator_MergesSampledCounts()
    {
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EntityRuntimeInfo { ActiveMessageCount = 7, IsApproximate = true });

        await using AzureServiceBusProvider provider = CreateProvider(
            EmulatorConnectionString,
            [new AspireModelDiscovery(["orders"], [new ModeledTopicInfo("billing-events", ["processor"])])],
            sampler);


        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }


        MessagingEntity queue = entities.Single(entity => entity.Kind == EntityKind.Queue);
        queue.RuntimeInfo.ShouldNotBeNull();
        queue.RuntimeInfo.ActiveMessageCount.ShouldBe(7);
        queue.RuntimeInfo.IsApproximate.ShouldBeTrue();

        MessagingEntity subscription = entities.Single(entity => entity.Kind == EntityKind.Subscription);
        subscription.RuntimeInfo.ShouldNotBeNull();

        MessagingEntity topic = entities.Single(entity => entity.Kind == EntityKind.Topic);
        topic.RuntimeInfo.ShouldBeNull();
        await sampler.DidNotReceive().SampleAsync("billing-events", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListEntitiesAsync_Emulator_SamplingFailure_KeepsEntities()
    {
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("broker unreachable"));

        await using AzureServiceBusProvider provider = CreateProvider(
            EmulatorConnectionString,
            [new AspireModelDiscovery(["orders"], [])],
            sampler);


        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }


        MessagingEntity queue = entities.ShouldHaveSingleItem();
        queue.Path.ShouldBe("orders");
        queue.RuntimeInfo.ShouldBeNull();
    }

    [Fact]
    public async Task CreateAsync_EmulatorProbeSucceeds_ReturnsProviderWithManagementCapabilities()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);


        await using AzureServiceBusProvider provider = await AzureServiceBusProvider.CreateAsync(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            management,
            CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
        await management.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
        await management.DidNotReceive().ListEntitiesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ProbeCompletedAtCreation_ListEntitiesDoesNotProbeAgain()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        management.ListEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns([new MessagingEntity { Path = "orders", Name = "orders", Kind = EntityKind.Queue }]);
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EntityRuntimeInfo());

        await using AzureServiceBusProvider provider = await AzureServiceBusProvider.CreateAsync(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            management,
            CancellationToken.None);


        await foreach (MessagingEntity _ in provider.ListEntitiesAsync(CancellationToken.None))
        {
        }


        await management.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_EmulatorProbeFails_KeepsDataPlaneOnlyCapabilities()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));


        await using AzureServiceBusProvider provider = await AzureServiceBusProvider.CreateAsync(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            management,
            CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeFalse();
        provider.Capabilities.CanPeek.ShouldBeTrue();
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_EmulatorProbeFails_ReportsManagementUnavailableReason()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint localhost:5300 could not be reached."));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management);


        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        provider.Capabilities.ManagementUnavailableReason.ShouldNotBeNull();
        provider.Capabilities.ManagementUnavailableReason.ShouldContain("localhost:5300 could not be reached");
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_FailedProbeWithinReprobeInterval_DoesNotProbeAgain()
    {
        FakeTimeProvider timeProvider = new();
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessagingProviderUnavailableException("down"));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management, timeProvider: timeProvider);
        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        timeProvider.Advance(AzureServiceBusProvider.ManagementReprobeInterval - TimeSpan.FromSeconds(1));
        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        await management.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_EmulatorComesUpAfterFailedProbe_EnablesManagementOnReprobe()
    {
        FakeTimeProvider timeProvider = new();
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new MessagingProviderUnavailableException("starting")), Task.CompletedTask);

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management, timeProvider: timeProvider);
        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        timeProvider.Advance(AzureServiceBusProvider.ManagementReprobeInterval);
        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        provider.Capabilities.CanManageEntities.ShouldBeTrue();
        provider.Capabilities.ManagementUnavailableReason.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ProbeSucceeded_NeverProbesAgain()
    {
        FakeTimeProvider timeProvider = new();
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, management: management, timeProvider: timeProvider);
        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        timeProvider.Advance(AzureServiceBusProvider.ManagementReprobeInterval * 4);
        await provider.ProbeCapabilitiesAsync(CancellationToken.None);


        await management.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListEntitiesAsync_EmulatorEverySampleUnreachable_ThrowsUnavailable()
    {
        IEmulatorEntityDiscovery discovery = Substitute.For<IEmulatorEntityDiscovery>();
        discovery.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MessagingEntity { Path = "orders", Name = "orders", Kind = EntityKind.Queue },
            new MessagingEntity { Path = "invoices", Name = "invoices", Kind = EntityKind.Queue },
        ]);
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("connection refused", ServiceBusFailureReason.ServiceCommunicationProblem));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, [discovery], sampler);


        await Should.ThrowAsync<MessagingProviderUnavailableException>(async () =>
        {
            await foreach (MessagingEntity _ in provider.ListEntitiesAsync(CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task ListEntitiesAsync_EmulatorOneSampleUnreachable_ReturnsTreeWithoutItsCounts()
    {
        IEmulatorEntityDiscovery discovery = Substitute.For<IEmulatorEntityDiscovery>();
        discovery.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MessagingEntity { Path = "orders", Name = "orders", Kind = EntityKind.Queue },
            new MessagingEntity { Path = "invoices", Name = "invoices", Kind = EntityKind.Queue },
        ]);
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync("orders", Arg.Any<CancellationToken>()).Returns(new EntityRuntimeInfo { ActiveMessageCount = 3 });
        sampler.SampleAsync("invoices", Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("timed out", ServiceBusFailureReason.ServiceTimeout));

        await using AzureServiceBusProvider provider = CreateProvider(EmulatorConnectionString, [discovery], sampler);
        List<MessagingEntity> entities = [];


        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }


        entities.Single(entity => entity.Path == "orders").RuntimeInfo!.ActiveMessageCount.ShouldBe(3);
        entities.Single(entity => entity.Path == "invoices").RuntimeInfo.ShouldBeNull();
    }

    private static AzureServiceBusProvider CreateProvider(
        string connectionString,
        IReadOnlyList<IEmulatorEntityDiscovery>? discoveryChain = null,
        IEmulatorRuntimeCountSampler? sampler = null,
        IAzureServiceBusManagement? management = null,
        TimeProvider? timeProvider = null)
        => new(
            connectionString,
            discoveryChain ?? [],
            NullLogger<AzureServiceBusProvider>.Instance,
            timeProvider ?? TimeProvider.System,
            management ?? UnreachableManagement(),
            sampler);

    // Default: no admin endpoint answers, so emulator listing goes through the discovery chain.
    private static IAzureServiceBusManagement UnreachableManagement()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessagingProviderUnavailableException("The admin endpoint could not be reached."));
        return management;
    }
}
