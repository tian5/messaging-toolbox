using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Emulator;

public class AspireModelDiscoveryTests
{
    private readonly AspireModelDiscovery _sut;

    public AspireModelDiscoveryTests()
    {
        _sut = new AspireModelDiscovery(
            ["orders", "billing-requests"],
            [new ModeledTopicInfo("billing-events", ["processor", "audit"])]);
    }

    [Fact]
    public async Task DiscoverAsync_ModeledEntities_ReturnsMappedEntitiesWithSubscriptionPaths()
    {
        IReadOnlyList<MessagingEntity>? entities = await _sut.DiscoverAsync(CancellationToken.None);


        entities.ShouldNotBeNull();
        entities.Count.ShouldBe(5);

        entities.Count(entity => entity.Kind == EntityKind.Queue).ShouldBe(2);
        entities.Count(entity => entity.Kind == EntityKind.Topic).ShouldBe(1);
        entities.Count(entity => entity.Kind == EntityKind.Subscription).ShouldBe(2);

        MessagingEntity subscription = entities.Single(entity => entity.Name == "processor");
        subscription.Path.ShouldBe("billing-events/subscriptions/processor");
        subscription.ParentPath.ShouldBe("billing-events");

        entities.ShouldAllBe(entity => entity.Status == MessagingEntityStatus.Active);
    }

    [Fact]
    public async Task DiscoverAsync_NoEntities_ReturnsEmptyList()
    {
        AspireModelDiscovery discovery = new([], []);


        IReadOnlyList<MessagingEntity>? entities = await discovery.DiscoverAsync(CancellationToken.None);


        entities.ShouldNotBeNull();
        entities.ShouldBeEmpty();
    }
}
