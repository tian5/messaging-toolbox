using System.Text;
using MessagingToolbox.Abstractions.Messaging;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Integration;

/// <summary>Real AMQP settlement round-trips against the Azure Service Bus emulator.</summary>
[Trait("Category", "Emulator")]
public class EmulatorIntegrationTests : IClassFixture<EmulatorFixture>
{
    private const string QueueName = EmulatorFixture.SettlementQueue;

    private const int MessageCount = 10;

    private readonly EmulatorFixture _fixture;

    public EmulatorIntegrationTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [EmulatorFact]
    public async Task SettlementViaLockTokens_DoesNotDamageOtherMessages()
    {
        await using AzureServiceBusProvider sut = _fixture.CreateProvider();

        List<ToolboxMessage> seed = Enumerable.Range(0, MessageCount)
            .Select(index => new ToolboxMessage { Body = Encoding.UTF8.GetBytes($"payload-{index}") })
            .ToList();

        await sut.SendAsync(QueueName, seed, CancellationToken.None);

        // Browsing must be side-effect free.
        IReadOnlyList<ToolboxMessage> browsed = await sut.PeekAsync(
            new PeekRequest(QueueName, MessageSubQueue.Active, MessageCount, null),
            CancellationToken.None);

        browsed.Count.ShouldBe(MessageCount);
        browsed.ShouldAllBe(message => message.DeliveryCount == 0);

        // Three dead-letter rounds against the same seeded shape: each round receives for
        // settlement, dead-letters exactly one message, and abandons the rest. Delivery counts
        // of the untouched messages grow by exactly one per round and never approach
        // MaxDeliveryCount (10), so the broker never dead-letters collateral.
        for (int round = 1; round <= 3; round++)
        {
            int remaining = MessageCount - (round - 1);

            IReadOnlyList<ToolboxMessage> received = await sut.ReceiveForSettlementAsync(
                QueueName, MessageSubQueue.Active, MessageCount, TimeSpan.FromSeconds(10), CancellationToken.None);

            received.Count.ShouldBe(remaining);

            ToolboxMessage target = received[remaining / 2];
            target.LockToken.ShouldNotBeNullOrWhiteSpace();

            IReadOnlyList<string> settled = await sut.SettleAsync(
                new SettleRequest(QueueName, MessageSubQueue.Active, SettlementOperation.DeadLetter, [target.LockToken], "integration-test", null),
                CancellationToken.None);

            settled.Count.ShouldBe(1);

            List<string> remainingTokens = received
                .Where(message => message.LockToken is not null && message.LockToken != target.LockToken)
                .Select(message => message.LockToken!)
                .ToList();

            await sut.SettleAsync(
                new SettleRequest(QueueName, MessageSubQueue.Active, SettlementOperation.Abandon, remainingTokens, null, null),
                CancellationToken.None);

            IReadOnlyList<ToolboxMessage> activeAfter = await sut.PeekAsync(
                new PeekRequest(QueueName, MessageSubQueue.Active, MessageCount, null),
                CancellationToken.None);

            activeAfter.Count.ShouldBe(remaining - 1);
            activeAfter.ShouldAllBe(message => message.DeliveryCount == round);

            IReadOnlyList<ToolboxMessage> deadLettered = await sut.PeekAsync(
                new PeekRequest(QueueName, MessageSubQueue.DeadLetter, MessageCount, null),
                CancellationToken.None);

            deadLettered.Count.ShouldBe(round);
            deadLettered.ShouldAllBe(message =>
                message.DeadLetterReason == "integration-test" &&
                message.DeadLetterReason != "MaxDeliveryCountExceeded");
        }
    }
}
