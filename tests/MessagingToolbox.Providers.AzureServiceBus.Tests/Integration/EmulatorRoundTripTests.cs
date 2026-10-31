using System.Text;
using MessagingToolbox.Abstractions.Messaging;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Integration;

/// <summary>
/// The preview release gate against the real emulator: what the trigger-and-observe flow relies on
/// (topic fan-out counts, sessions, dead-letter requeue, deferred receive, the admin plane).
/// </summary>
[Trait("Category", "Emulator")]
public class EmulatorRoundTripTests : IClassFixture<EmulatorFixture>
{
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(5);

    private readonly EmulatorFixture _fixture;

    public EmulatorRoundTripTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [EmulatorFact]
    public async Task ListEntitiesAsync_AdminEndpointReachable_EnablesManagementAndReportsSessions()
    {
        await using AzureServiceBusProvider sut = _fixture.CreateProvider();


        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in sut.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }


        sut.Capabilities.CanManageEntities.ShouldBeTrue();
        entities.Single(entity => entity.Path == EmulatorFixture.SessionQueue).RequiresSession.ShouldBeTrue();
        entities.ShouldContain(entity => entity.Path == EmulatorFixture.SubscriptionB);
        entities.Single(entity => entity.Path == EmulatorFixture.SessionQueue).Properties
            .ShouldContain(new EntityProperty("Settings", "Sessions", "yes"));
        entities.Single(entity => entity.Path == EmulatorFixture.SubscriptionB).Properties
            .ShouldContain(property => property.Name == "Lock duration");
    }

    [EmulatorFact]
    public async Task SendAsync_ToTopic_RefreshesEverySubscriptionCount()
    {
        await using AzureServiceBusProvider sut = _fixture.CreateProvider();
        (await sut.GetRuntimeInfoAsync(EmulatorFixture.SubscriptionA, CancellationToken.None)).ActiveMessageCount.ShouldBe(0);
        (await sut.GetRuntimeInfoAsync(EmulatorFixture.SubscriptionB, CancellationToken.None)).ActiveMessageCount.ShouldBe(0);


        await sut.SendAsync(EmulatorFixture.Topic, Messages(3), CancellationToken.None);


        (await sut.GetRuntimeInfoAsync(EmulatorFixture.SubscriptionA, CancellationToken.None)).ActiveMessageCount.ShouldBe(3);
        (await sut.GetRuntimeInfoAsync(EmulatorFixture.SubscriptionB, CancellationToken.None)).ActiveMessageCount.ShouldBe(3);
    }

    [EmulatorFact]
    public async Task SessionQueue_PeekThenReceiveOneSession_PeekLocksNothingAndSettlesOnlyThatSession()
    {
        await using AzureServiceBusProvider sut = _fixture.CreateProvider();
        await sut.SendAsync(EmulatorFixture.SessionQueue, [.. Messages(2, "s1"), .. Messages(1, "s2")], CancellationToken.None);


        IReadOnlyList<ToolboxMessage> peeked = await sut.PeekAsync(
            new PeekRequest(EmulatorFixture.SessionQueue, MessageSubQueue.Active, 10, null), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> peekedS1 = await sut.PeekAsync(
            new PeekRequest(EmulatorFixture.SessionQueue, MessageSubQueue.Active, 10, null, "s1"), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveForSettlementAsync(
            EmulatorFixture.SessionQueue, MessageSubQueue.Active, 10, ReceiveWait, CancellationToken.None, sessionId: "s1");
        IReadOnlyList<string> settled = await sut.SettleAsync(
            new SettleRequest(EmulatorFixture.SessionQueue, MessageSubQueue.Active, SettlementOperation.Complete, [.. received.Select(message => message.LockToken!)], null, null),
            CancellationToken.None);


        peeked.Count.ShouldBe(3);
        peekedS1.ShouldAllBe(message => message.SessionId == "s1");
        peekedS1.Count.ShouldBe(2);
        received.Count.ShouldBe(2);
        received.ShouldAllBe(message => message.SessionId == "s1" && message.DeliveryCount == 1);
        settled.Count.ShouldBe(2);
        IReadOnlyList<ToolboxMessage> left = await sut.PeekAsync(
            new PeekRequest(EmulatorFixture.SessionQueue, MessageSubQueue.Active, 10, null), CancellationToken.None);
        left.ShouldHaveSingleItem().SessionId.ShouldBe("s2");
    }

    [EmulatorFact]
    public async Task DeadLetterQueue_ResubmitCloneThenRemoveOriginal_MovesMessageBackToActive()
    {
        await using AzureServiceBusProvider sut = _fixture.CreateProvider();
        await sut.SendAsync(EmulatorFixture.PlainQueue, Messages(1), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveForSettlementAsync(
            EmulatorFixture.PlainQueue, MessageSubQueue.Active, 1, ReceiveWait, CancellationToken.None);
        await sut.SettleAsync(
            new SettleRequest(EmulatorFixture.PlainQueue, MessageSubQueue.Active, SettlementOperation.DeadLetter, [received.Single().LockToken!], "Poison", "Bad payload"),
            CancellationToken.None);
        ToolboxMessage deadLettered = (await sut.PeekAsync(
            new PeekRequest(EmulatorFixture.PlainQueue, MessageSubQueue.DeadLetter, 10, null), CancellationToken.None)).ShouldHaveSingleItem();


        ToolboxMessage clone = await sut.PrepareResubmitAsync(
            EmulatorFixture.PlainQueue, MessageSubQueue.DeadLetter, deadLettered, ResubmitOverrides.Empty, CancellationToken.None);
        await sut.SendAsync(EmulatorFixture.PlainQueue, [clone], CancellationToken.None);
        IReadOnlyList<long> removed = await sut.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest(EmulatorFixture.PlainQueue, MessageSubQueue.DeadLetter, SettlementOperation.Complete, [deadLettered.SequenceNumber!.Value], null, null),
            CancellationToken.None);


        deadLettered.DeadLetterReason.ShouldBe("Poison");
        deadLettered.DeadLetterErrorDescription.ShouldBe("Bad payload");
        removed.ShouldBe([deadLettered.SequenceNumber!.Value]);
        (await sut.PeekAsync(new PeekRequest(EmulatorFixture.PlainQueue, MessageSubQueue.DeadLetter, 10, null), CancellationToken.None)).ShouldBeEmpty();
        ToolboxMessage active = (await sut.PeekAsync(
            new PeekRequest(EmulatorFixture.PlainQueue, MessageSubQueue.Active, 10, null), CancellationToken.None)).ShouldHaveSingleItem();
        active.Body.ShouldBe(deadLettered.Body);
        active.DeliveryCount.ShouldBe(0);
    }

    [EmulatorFact]
    public async Task DeferredMessage_ReceiveBySequenceNumberAndComplete_LeavesQueueEmpty()
    {
        await using AzureServiceBusProvider sut = _fixture.CreateProvider();
        await sut.SendAsync(EmulatorFixture.DeferredQueue, Messages(1), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveForSettlementAsync(
            EmulatorFixture.DeferredQueue, MessageSubQueue.Active, 1, ReceiveWait, CancellationToken.None);
        await sut.SettleAsync(
            new SettleRequest(EmulatorFixture.DeferredQueue, MessageSubQueue.Active, SettlementOperation.Defer, [received.Single().LockToken!], null, null),
            CancellationToken.None);
        ToolboxMessage deferred = (await sut.PeekAsync(
            new PeekRequest(EmulatorFixture.DeferredQueue, MessageSubQueue.Active, 10, null), CancellationToken.None)).ShouldHaveSingleItem();


        IReadOnlyList<ToolboxMessage> back = await sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest(EmulatorFixture.DeferredQueue, MessageSubQueue.Active, [new DeferredMessageReference(deferred.SequenceNumber!.Value)]),
            CancellationToken.None);
        IReadOnlyList<string> completed = await sut.SettleAsync(
            new SettleRequest(EmulatorFixture.DeferredQueue, MessageSubQueue.Active, SettlementOperation.Complete, [back.Single().LockToken!], null, null),
            CancellationToken.None);


        deferred.State.ShouldBe(MessageState.Deferred);
        back.ShouldHaveSingleItem().SequenceNumber.ShouldBe(deferred.SequenceNumber);
        completed.Count.ShouldBe(1);
        (await sut.PeekAsync(new PeekRequest(EmulatorFixture.DeferredQueue, MessageSubQueue.Active, 10, null), CancellationToken.None)).ShouldBeEmpty();
    }

    private static List<ToolboxMessage> Messages(int count, string? sessionId = null)
        => [.. Enumerable.Range(0, count).Select(index => new ToolboxMessage
        {
            Body = Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid():N}-{index}"),
            SessionId = sessionId,
        })];
}
