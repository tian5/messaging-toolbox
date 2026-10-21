using System.Text;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Integration;

/// <summary>
/// Reproduction for the count regression: repeated and concurrent runtime-count sampling against
/// the real emulator must return stable, correct counts (cached receivers, TTL invalidation).
/// No admin plane here, so every count comes from the peek sampler.
/// </summary>
[Trait("Category", "Emulator")]
public class EmulatorSamplerReproTests : IClassFixture<EmulatorFixture>
{
    private readonly EmulatorFixture _fixture;

    public EmulatorSamplerReproTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [EmulatorFact]
    public async Task CountSampling_ResampleAfterInvalidate_ReturnsCorrectCounts()
    {
        const string queue = EmulatorFixture.ResampleQueue;
        await using AzureServiceBusProvider sut = CreateProviderWithoutAdminPlane();
        await sut.SendAsync(queue, Messages(10), CancellationToken.None);

        EntityRuntimeInfo first = await sut.GetRuntimeInfoAsync(queue, CancellationToken.None);
        first.ActiveMessageCount.ShouldBe(10);


        // A mutation invalidates the sampler cache, so the next sample re-walks with the
        // sampler's cached receiver — the regression suspects receiver reuse breaks the walk.
        await sut.SendAsync(queue, Messages(1), CancellationToken.None);

        EntityRuntimeInfo second = await sut.GetRuntimeInfoAsync(queue, CancellationToken.None);
        second.ActiveMessageCount.ShouldBe(11);


        await sut.ReceiveAsync(
            new ReceiveRequest(queue, MessageSubQueue.Active, 2, TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        EntityRuntimeInfo third = await sut.GetRuntimeInfoAsync(queue, CancellationToken.None);
        third.ActiveMessageCount.ShouldBe(9);
    }

    [EmulatorFact]
    public async Task CountSampling_ConcurrentSamplesOfSameEntity_AllReturnCorrectCounts()
    {
        const string queue = EmulatorFixture.ConcurrentQueue;
        await using AzureServiceBusProvider sut = CreateProviderWithoutAdminPlane();
        await sut.SendAsync(queue, Messages(10), CancellationToken.None);

        EntityRuntimeInfo warm = await sut.GetRuntimeInfoAsync(queue, CancellationToken.None);
        warm.ActiveMessageCount.ShouldBe(10);


        // Invalidate via mutation, then race several samples: the cache check-then-act lets them
        // all miss and walk the same cached receiver concurrently.
        await sut.SendAsync(queue, Messages(1), CancellationToken.None);

        EntityRuntimeInfo[] results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => sut.GetRuntimeInfoAsync(queue, CancellationToken.None)));

        results.ShouldAllBe(info => info.ActiveMessageCount == 11);
    }

    [EmulatorFact]
    public async Task ListEntities_RepeatedTreeLoadsAfterMutations_KeepCorrectCounts()
    {
        const string queue = EmulatorFixture.TreeQueue;
        await using AzureServiceBusProvider sut = CreateProviderWithoutAdminPlane();
        await sut.SendAsync(queue, Messages(10), CancellationToken.None);

        for (int round = 1; round <= 3; round++)
        {
            List<MessagingEntity> entities = [];
            await foreach (MessagingEntity entity in sut.ListEntitiesAsync(CancellationToken.None))
            {
                entities.Add(entity);
            }

            MessagingEntity listed = entities.Single(entity => entity.Path == queue);
            listed.RuntimeInfo.ShouldNotBeNull();
            listed.RuntimeInfo.ActiveMessageCount.ShouldBe(10 + round - 1, $"round {round}");

            await sut.SendAsync(queue, Messages(1), CancellationToken.None);
        }
    }

    // Listing must come from the modeled entities via the discovery chain.
    private AzureServiceBusProvider CreateProviderWithoutAdminPlane()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessagingProviderUnavailableException("No admin endpoint in this test."));

        return _fixture.CreateProvider(management);
    }

    private static List<ToolboxMessage> Messages(int count)
        => [.. Enumerable.Range(0, count)
            .Select(index => new ToolboxMessage { Body = Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid():N}-{index}") })];
}
