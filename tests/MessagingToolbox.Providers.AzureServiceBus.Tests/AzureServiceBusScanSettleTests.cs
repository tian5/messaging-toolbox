using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusScanSettleTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    [Fact]
    public async Task SettleBySequenceNumbersAsync_MatchingSequence_SettlesWithRequestedOperation()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceivedMessage target = CreateMessage(5);
        ServiceBusReceivedMessage other = CreateMessage(9);
        ServiceBusReceiver receiver = CreateReceiver([new[] { target, other }, []]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.DeadLetter, [5], "poison", "bad payload"),
            CancellationToken.None);


        settled.ShouldBe([5]);
        await receiver.Received(1).DeadLetterMessageAsync(target, "poison", "bad payload", Arg.Any<CancellationToken>());
        // First-sight non-targets are held during the walk and abandoned when the scan ends.
        await receiver.Received(1).AbandonMessageAsync(other, cancellationToken: Arg.Any<CancellationToken>());
        await receiver.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_TailTarget_DeepQueueReachesTargetInOnePass()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        // 250-message DLQ, 100-message batches, target at position 237 — the regression case:
        // the old abandon-and-cycle logic never got past the head batch.
        List<ServiceBusReceivedMessage> all = Enumerable.Range(1, 250).Select(sequence => CreateMessage(sequence)).ToList();
        ServiceBusReceivedMessage target = all[236];
        ServiceBusReceiver receiver = CreateReceiver(
        [
            all.Take(100).ToArray(),
            all.Skip(100).Take(100).ToArray(),
            all.Skip(200).ToArray(),
            [],
        ]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [237], null, null),
            CancellationToken.None);


        settled.ShouldBe([237]);
        await receiver.Received(1).CompleteMessageAsync(target, Arg.Any<CancellationToken>());
        // The 249 non-targets were held for the walk and released when it ended.
        await receiver.Received(249).AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), cancellationToken: Arg.Any<CancellationToken>());
        await receiver.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_SecondCallWhileLocksRelease_SettlesAfterGrace()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceivedMessage first = CreateMessage(1);
        ServiceBusReceivedMessage firstTarget = CreateMessage(2);
        ServiceBusReceivedMessage third = CreateMessage(3);
        ServiceBusReceiver firstCall = CreateReceiver([new[] { first, firstTarget, third }, []]);
        // The second scan starts while the first scan's locks are still releasing (lazy release):
        // its first receives come back empty, then the messages become available again.
        ServiceBusReceiver secondCall = CreateReceiver([[], [], new[] { first, firstTarget, third }, []]);
        int factoryCalls = 0;
        provider.ReceiverFactory = (_, _, _, _) => ++factoryCalls == 1 ? firstCall : secondCall;


        IReadOnlyList<long> firstSettled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [2], null, null),
            CancellationToken.None);

        IReadOnlyList<long> secondSettled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [3], null, null),
            CancellationToken.None);


        firstSettled.ShouldBe([2]);
        secondSettled.ShouldBe([3]);
        factoryCalls.ShouldBe(2);
        await firstCall.Received(1).CompleteMessageAsync(firstTarget, Arg.Any<CancellationToken>());
        await secondCall.Received(1).CompleteMessageAsync(third, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_ConsecutiveScans_FirstScanReleasesHeldLocksBeforeDispose()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceivedMessage first = CreateMessage(1);
        ServiceBusReceivedMessage firstTarget = CreateMessage(2);
        ServiceBusReceivedMessage third = CreateMessage(3);
        ServiceBusReceiver firstCall = CreateReceiver([new[] { first, firstTarget, third }]);
        // The second scan's receiver serves the remaining messages immediately — no empty-batch
        // grace — which only works when the first scan released its held locks itself instead of
        // leaving them to lock expiry (the reported bug: the second requeue kept its original).
        ServiceBusReceiver secondCall = CreateReceiver([new[] { first, third }, []]);
        int factoryCalls = 0;
        provider.ReceiverFactory = (_, _, _, _) => ++factoryCalls == 1 ? firstCall : secondCall;


        IReadOnlyList<long> firstSettled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [2], null, null),
            CancellationToken.None);

        IReadOnlyList<long> secondSettled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [3], null, null),
            CancellationToken.None);


        firstSettled.ShouldBe([2]);
        secondSettled.ShouldBe([3]);
        // The first scan abandons its held non-targets before closing the receiver.
        Received.InOrder(async () =>
        {
            await firstCall.AbandonMessageAsync(first, cancellationToken: Arg.Any<CancellationToken>());
            await firstCall.AbandonMessageAsync(third, cancellationToken: Arg.Any<CancellationToken>());
            await firstCall.DisposeAsync();
        });
        await secondCall.Received(1).CompleteMessageAsync(third, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_EmptySubQueue_ReturnsEmpty()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceiver receiver = CreateReceiver([[]]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [42], null, null),
            CancellationToken.None);


        settled.ShouldBeEmpty();
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_AbsentTarget_TerminatesAfterOnePass()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceivedMessage first = CreateMessage(7);
        ServiceBusReceivedMessage second = CreateMessage(8);
        ServiceBusReceiver receiver = CreateReceiver([new[] { first, second }, []]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [42], null, null),
            CancellationToken.None);


        settled.ShouldBeEmpty();
        // One content batch, then the empty-batch grace runs out: 1 + (ScanSettleMaxEmptyBatches + 1) receives.
        await receiver.Received(5).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_HeadTargetOnBrokerThatReServesLockedMessages_SettlesImmediately()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        // Emulator-style semantics: the broker does NOT skip locked messages and would re-serve
        // the same batch forever. The target is in the head batch, so it must settle on first
        // sight — before any cycle logic runs.
        ServiceBusReceivedMessage target = CreateMessage(5);
        ServiceBusReceivedMessage other = CreateMessage(9);
        ServiceBusReceiver receiver = CreateReceiver([new[] { target, other }, new[] { other, target }]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [5], null, null),
            CancellationToken.None);


        settled.ShouldBe([5]);
        await receiver.Received(1).CompleteMessageAsync(target, Arg.Any<CancellationToken>());
        await receiver.Received(1).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_AbsentTargetRepeatingBatch_StopsAtCycleBreak()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        // Broker keeps re-serving the same head batch (no lock skipping). After the first sight
        // the batch is all-seen, so the scan must stop at the cycle break, not spin to the cap.
        ServiceBusReceivedMessage first = CreateMessage(7);
        ServiceBusReceivedMessage second = CreateMessage(8);
        ServiceBusReceiver receiver = CreateReceiver([new[] { first, second }, new[] { first, second }]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [42], null, null),
            CancellationToken.None);


        settled.ShouldBeEmpty();
        await receiver.Received(2).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_LockExpiryRedelivery_AbandonsDuplicate()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceivedMessage duplicate = CreateMessage(7);
        ServiceBusReceiver receiver = CreateReceiver([new[] { duplicate }, new[] { duplicate }, []]);
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [42], null, null),
            CancellationToken.None);


        settled.ShouldBeEmpty();
        // The re-seen duplicate's replacement lock is released mid-walk; the originally held copy
        // is released when the scan ends.
        await receiver.Received(2).AbandonMessageAsync(duplicate, cancellationToken: Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_TargetsNeverFound_StopsAtScanCap()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        List<ServiceBusReceivedMessage> nonMatches = Enumerable.Range(0, 10_000).Select(sequence => CreateMessage(10_000 + sequence)).ToList();
        ServiceBusReceiver receiver = CreateReceiver(
            Enumerable.Range(0, 100).Select(index => nonMatches.Skip(index * 100).Take(100).ToArray()).ToArray());
        provider.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<long> settled = await provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [1], null, null),
            CancellationToken.None);


        settled.ShouldBeEmpty();
        // Every scanned message was a first sight, so all were held — the cap is the only exit —
        // and every held lock is released when the walk ends.
        await receiver.Received(100).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.Received(10_000).AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), cancellationToken: Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleBySequenceNumbersAsync_ContendingCallCancelledWhileWaitingForReceiverLock_ThrowsOperationCanceled()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IReadOnlyList<ServiceBusReceivedMessage>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                entered.TrySetResult();
                return gate.Task;
            });
        provider.ReceiverFactory = (_, _, _, _) => receiver;

        Task<IReadOnlyList<long>> first = provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [1], null, null),
            CancellationToken.None);
        await entered.Task;


        using CancellationTokenSource cancellation = new();
        Task<IReadOnlyList<long>> second = provider.SettleBySequenceNumbersAsync(
            new SettleBySequenceRequest("orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [2], null, null),
            cancellation.Token);
        await cancellation.CancelAsync();


        // The contending scan waits on the per-entity receiver lock and must be unblocked by
        // cancellation instead of hanging behind the first scan.
        await Should.ThrowAsync<OperationCanceledException>(() => second);
        gate.SetResult([]);
        (await first).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MessageSubQueue.Active)]
    [InlineData(MessageSubQueue.Scheduled)]
    public async Task SettleBySequenceNumbersAsync_NonDeadLetterSubQueue_ThrowsNotSupported(MessageSubQueue subQueue)
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        provider.ReceiverFactory = (_, _, _, _) => CreateReceiver([]);


        NotSupportedByProviderException exception = await Should.ThrowAsync<NotSupportedByProviderException>(
            () => provider.SettleBySequenceNumbersAsync(
                new SettleBySequenceRequest("orders", subQueue, SettlementOperation.Complete, [5], null, null),
                CancellationToken.None));


        exception.Message.ShouldContain("dead-letter");
    }

    private static AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            Substitute.For<IAzureServiceBusManagement>());

    private static ServiceBusReceivedMessage CreateMessage(long sequenceNumber)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData($"{{\"seq\": {sequenceNumber}}}"),
            messageId: $"msg-{sequenceNumber}",
            sequenceNumber: sequenceNumber,
            lockTokenGuid: Guid.NewGuid());

    private static ServiceBusReceiver CreateReceiver(IReadOnlyList<IReadOnlyList<ServiceBusReceivedMessage>> batches)
    {
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(batches.First(), batches.Skip(1).Select(batch => batch).ToArray());
        return receiver;
    }
}
