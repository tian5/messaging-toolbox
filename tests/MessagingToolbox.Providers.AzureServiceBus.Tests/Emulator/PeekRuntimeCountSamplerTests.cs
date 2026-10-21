using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Emulator;

public class PeekRuntimeCountSamplerTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<ServiceBusReceiver> _createdReceivers = [];
    private int _peekCalls;

    [Fact]
    public async Task SampleAsync_SecondCallWithinTtl_ServesCachedCountsWithoutPeekingAgain()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1), CreateMessage(2)]);


        EntityRuntimeInfo first = await sut.SampleAsync("orders", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;
        EntityRuntimeInfo second = await sut.SampleAsync("orders", CancellationToken.None);


        first.ActiveMessageCount.ShouldBe(2);
        second.ShouldBeSameAs(first);
        _peekCalls.ShouldBe(peeksAfterFirstSample);
        // Both sub-queues (active + dead-letter) were sampled by the first call.
        peeksAfterFirstSample.ShouldBeGreaterThan(0);
        _createdReceivers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SampleAsync_WithinTtlAfterTimePasses_StillServesCachedCounts()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;


        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        await sut.SampleAsync("orders", CancellationToken.None);


        _peekCalls.ShouldBe(peeksAfterFirstSample);
    }

    [Fact]
    public async Task SampleAsync_AfterTtlExpires_ResamplesEntity()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;


        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        await sut.SampleAsync("orders", CancellationToken.None);


        _peekCalls.ShouldBeGreaterThan(peeksAfterFirstSample);
    }

    [Fact]
    public async Task Invalidate_CachedEntity_ForcesResampleOnNextCall()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;

        sut.Invalidate("orders");
        await sut.SampleAsync("orders", CancellationToken.None);

        _peekCalls.ShouldBeGreaterThan(peeksAfterFirstSample);
        // Receivers are deliberately NOT cached: every sample walks active + dead-letter with
        // fresh receivers, because a reused emulator receiver resumes mid-queue and undercounts.
        _createdReceivers.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Invalidate_OtherEntity_KeepsCachedCounts()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;


        sut.Invalidate("billing");
        await sut.SampleAsync("orders", CancellationToken.None);


        _peekCalls.ShouldBe(peeksAfterFirstSample);
    }

    [Fact]
    public async Task Invalidate_Topic_ForcesResampleOfItsSubscriptions()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders/subscriptions/audit", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;


        sut.Invalidate("orders");
        Task<EntityRuntimeInfo> resample = sut.SampleAsync("orders/subscriptions/audit", CancellationToken.None);
        _timeProvider.Advance(PeekRuntimeCountSampler.FanOutSettleTime);
        await resample;


        _peekCalls.ShouldBeGreaterThan(peeksAfterFirstSample);
    }

    [Fact]
    public async Task SampleAsync_SubscriptionRightAfterTopicInvalidate_WaitsForFanOutBeforePeeking()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        sut.Invalidate("orders");


        Task<EntityRuntimeInfo> sample = sut.SampleAsync("orders/subscriptions/audit", CancellationToken.None);
        int peeksBeforeFanOutSettled = _peekCalls;
        _timeProvider.Advance(PeekRuntimeCountSampler.FanOutSettleTime);
        EntityRuntimeInfo info = await sample;


        peeksBeforeFanOutSettled.ShouldBe(0);
        info.ActiveMessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task SampleAsync_QueueRightAfterInvalidate_PeeksWithoutWaiting()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        sut.Invalidate("orders");


        EntityRuntimeInfo info = await sut.SampleAsync("orders", CancellationToken.None);


        info.ActiveMessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task Invalidate_Subscription_KeepsSiblingSubscriptionCounts()
    {
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders/subscriptions/billing", CancellationToken.None);
        int peeksAfterFirstSample = _peekCalls;


        sut.Invalidate("orders/subscriptions/audit");
        await sut.SampleAsync("orders/subscriptions/billing", CancellationToken.None);


        _peekCalls.ShouldBe(peeksAfterFirstSample);
    }

    [Fact]
    public async Task Invalidate_DuringInFlightWalk_DoesNotCacheTheStaleResult()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)], gate.Task);
        Task<EntityRuntimeInfo> inFlight = sut.SampleAsync("orders", CancellationToken.None);


        sut.Invalidate("orders");
        gate.SetResult();
        await inFlight;
        int peeksAfterStaleWalk = _peekCalls;
        await sut.SampleAsync("orders", CancellationToken.None);


        _peekCalls.ShouldBeGreaterThan(peeksAfterStaleWalk);
    }

    [Fact]
    public async Task SampleAsync_ConcurrentCalls_ShareOneWalk()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)], gate.Task);
        Task<EntityRuntimeInfo> first = sut.SampleAsync("orders", CancellationToken.None);
        Task<EntityRuntimeInfo> second = sut.SampleAsync("orders", CancellationToken.None);


        gate.SetResult();
        EntityRuntimeInfo[] results = await Task.WhenAll(first, second);


        results[1].ShouldBeSameAs(results[0]);
        _createdReceivers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SampleAsync_SharedWalkCallerCancelled_OtherCallerStillGetsCounts()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)], gate.Task);
        using CancellationTokenSource firstCallerCancellation = new();
        Task<EntityRuntimeInfo> cancelled = sut.SampleAsync("orders", firstCallerCancellation.Token);
        Task<EntityRuntimeInfo> surviving = sut.SampleAsync("orders", CancellationToken.None);


        await firstCallerCancellation.CancelAsync();
        gate.SetResult();


        await Should.ThrowAsync<OperationCanceledException>(cancelled);
        (await surviving).ActiveMessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task SampleAsync_ScheduledMessage_CountsItSeparatelyFromActive()
    {
        ServiceBusReceivedMessage scheduled = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("{}"),
            messageId: "msg-scheduled",
            sequenceNumber: 9,
            scheduledEnqueueTime: _timeProvider.GetUtcNow().AddHours(1));
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1), scheduled]);


        EntityRuntimeInfo info = await sut.SampleAsync("orders", CancellationToken.None);


        info.ActiveMessageCount.ShouldBe(1);
        info.ScheduledMessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task SampleAsync_DeferredMessage_CountsItAsActiveAndReportsItSeparately()
    {
        ServiceBusReceivedMessage deferred = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("{}"),
            messageId: "msg-deferred",
            sequenceNumber: 9,
            serviceBusMessageState: ServiceBusMessageState.Deferred);
        await using PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1), deferred]);


        EntityRuntimeInfo info = await sut.SampleAsync("orders", CancellationToken.None);


        info.ActiveMessageCount.ShouldBe(2);
        info.DeferredMessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_CachedReceivers_AreDisposed()
    {
        PeekRuntimeCountSampler sut = CreateSampler([CreateMessage(1)]);
        await sut.SampleAsync("orders", CancellationToken.None);


        await sut.DisposeAsync();


        foreach (ServiceBusReceiver receiver in _createdReceivers)
        {
            await receiver.Received(1).DisposeAsync();
        }
    }

    private PeekRuntimeCountSampler CreateSampler(IReadOnlyList<ServiceBusReceivedMessage> activeMessages, Task? peekGate = null)
    {
        PeekRuntimeCountSampler sampler = new(new ServiceBusClient(EmulatorConnectionString), _timeProvider);
        sampler.ReceiverFactory = (_, _, subQueue) =>
        {
            ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
            IReadOnlyList<ServiceBusReceivedMessage> messages =
                subQueue == SubQueue.None ? activeMessages : [];
            receiver.PeekMessagesAsync(Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    Interlocked.Increment(ref _peekCalls);
                    if (peekGate is not null)
                    {
                        await peekGate;
                    }

                    // First page from the head, empty afterwards — mirrors an exhausted sub-queue.
                    return callInfo.ArgAt<long?>(1) is null
                        ? messages
                        : (IReadOnlyList<ServiceBusReceivedMessage>)[];
                });
            lock (_createdReceivers)
            {
                _createdReceivers.Add(receiver);
            }

            return receiver;
        };

        return sampler;
    }

    private static ServiceBusReceivedMessage CreateMessage(long sequenceNumber)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData($"{{\"seq\": {sequenceNumber}}}"),
            messageId: $"msg-{sequenceNumber}",
            sequenceNumber: sequenceNumber);
}
