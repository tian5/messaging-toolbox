using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusSessionTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private static readonly DateTimeOffset SessionLockedUntil = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    // The broker's wording for a non-session receive on a session entity (verified against the emulator).
    private static readonly InvalidOperationException NonSessionReceiveRejected =
        new("It is not possible for an entity that requires sessions to create a non-sessionful message receiver.");

    private static readonly ServiceBusException NoSessionAvailable = new("No session available.", ServiceBusFailureReason.ServiceTimeout);

    [Fact]
    public async Task ReceiveAsync_EntityRequiresSessions_ReceivesFromEachAvailableSession()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[CreateMessage(1, "A"), CreateMessage(2, "A")], []]);
        ServiceBusSessionReceiver sessionB = CreateSessionReceiver("B", [[CreateMessage(3, "B")], []]);
        List<string?> requestedSessions = [];
        sut.SessionReceiverFactory = SessionSequence(requestedSessions, sessionA, sessionB);


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveAsync(
            new ReceiveRequest("orders", MessageSubQueue.Active, 50, TimeSpan.FromSeconds(10)),
            CancellationToken.None);


        received.Select(message => message.SessionId).ShouldBe(["A", "A", "B"]);
        requestedSessions.ShouldAllBe(sessionId => sessionId == null);
        await sessionA.Received(1).DisposeAsync();
        await sessionB.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReceiveAsync_SessionRequirementLearned_SkipsPlainReceiverNextTime()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        int plainReceivers = 0;
        sut.ReceiverFactory = (_, _, _, _) =>
        {
            plainReceivers++;
            return RejectingReceiver();
        };
        sut.SessionReceiverFactory = SessionSequence([]);
        ReceiveRequest request = new("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1));
        await sut.ReceiveAsync(request, CancellationToken.None);


        await sut.ReceiveAsync(request, CancellationToken.None);


        plainReceivers.ShouldBe(1);
    }

    [Fact]
    public async Task ReceiveAsync_NamedSession_AcceptsOnlyThatSessionAndWaitsFullTime()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusSessionReceiver sessionB = CreateSessionReceiver("B", [[CreateMessage(3, "B")], []]);
        List<string?> requestedSessions = [];
        sut.SessionReceiverFactory = SessionSequence(requestedSessions, sessionB);


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveAsync(
            new ReceiveRequest("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(10), "B"),
            CancellationToken.None);


        received.ShouldHaveSingleItem().SessionId.ShouldBe("B");
        requestedSessions.ShouldBe(["B"]);
        await sessionB.Received(1).ReceiveMessagesAsync(Arg.Any<int>(), TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAsync_SessionFilterOnDeadLetter_ThrowsNotSupported()
    {
        await using AzureServiceBusProvider sut = CreateProvider();


        await Should.ThrowAsync<NotSupportedByProviderException>(() => sut.ReceiveAsync(
            new ReceiveRequest("orders", MessageSubQueue.DeadLetter, 10, TimeSpan.FromSeconds(1), "A"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ReceiveForSettlementAsync_SessionMessages_ReportSessionLockAndHoldSession()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[CreateMessage(1, "A", Guid.NewGuid())], []]);
        sut.SessionReceiverFactory = SessionSequence([], sessionA);


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveForSettlementAsync(
            "orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None);


        ToolboxMessage message = received.ShouldHaveSingleItem();
        message.LockedUntil.ShouldBe(SessionLockedUntil);
        IReadOnlyDictionary<string, DateTimeOffset> states = await sut.GetHeldLockStatesAsync(
            "orders", MessageSubQueue.Active, [message.LockToken!], CancellationToken.None);
        states[message.LockToken!].ShouldBe(SessionLockedUntil);
        await sessionA.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task SettleAsync_LastMessageOfHeldSession_SettlesThroughSessionAndReleasesIt()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusReceivedMessage first = CreateMessage(1, "A", Guid.NewGuid());
        ServiceBusReceivedMessage second = CreateMessage(2, "A", Guid.NewGuid());
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[first, second], []]);
        sut.SessionReceiverFactory = SessionSequence([], sessionA);
        await sut.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None);
        await sut.SettleAsync(CompleteRequest(first.LockToken), CancellationToken.None);
        await sessionA.DidNotReceive().DisposeAsync();


        IReadOnlyList<string> settled = await sut.SettleAsync(CompleteRequest(second.LockToken), CancellationToken.None);


        settled.ShouldBe([second.LockToken]);
        await sessionA.Received(1).CompleteMessageAsync(first, Arg.Any<CancellationToken>());
        await sessionA.Received(1).CompleteMessageAsync(second, Arg.Any<CancellationToken>());
        await sessionA.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task SettleAsync_SessionLockLost_ForgetsEveryMessageOfThatSession()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusReceivedMessage first = CreateMessage(1, "A", Guid.NewGuid());
        ServiceBusReceivedMessage second = CreateMessage(2, "A", Guid.NewGuid());
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[first, second], []]);
        sessionA.CompleteMessageAsync(first, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("lost", ServiceBusFailureReason.SessionLockLost));
        sut.SessionReceiverFactory = SessionSequence([], sessionA);
        await sut.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None);


        IReadOnlyList<string> settled = await sut.SettleAsync(CompleteRequest(first.LockToken, second.LockToken), CancellationToken.None);


        settled.ShouldBeEmpty();
        await sessionA.DidNotReceive().CompleteMessageAsync(second, Arg.Any<CancellationToken>());
        await sessionA.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task GetHeldLockStatesAsync_SessionLockExpiredBeyondGrace_ReleasesSession()
    {
        FakeTimeProvider timeProvider = new(SessionLockedUntil.AddSeconds(-30));
        await using AzureServiceBusProvider sut = CreateProvider(timeProvider);
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusReceivedMessage message = CreateMessage(1, "A", Guid.NewGuid());
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[message], []]);
        sut.SessionReceiverFactory = SessionSequence([], sessionA);
        await sut.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None);


        timeProvider.Advance(TimeSpan.FromMinutes(3));
        IReadOnlyDictionary<string, DateTimeOffset> states = await sut.GetHeldLockStatesAsync(
            "orders", MessageSubQueue.Active, [message.LockToken], CancellationToken.None);


        states.ShouldBeEmpty();
        await sessionA.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReceiveForSettlementAsync_RenewalRequested_RenewsSessionLockNotMessageLocks()
    {
        FakeTimeProvider timeProvider = new(SessionLockedUntil.AddSeconds(-5));
        await using AzureServiceBusProvider sut = CreateProvider(timeProvider);
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[CreateMessage(1, "A", Guid.NewGuid())], []]);
        sut.SessionReceiverFactory = SessionSequence([], sessionA);
        await sut.ReceiveForSettlementAsync(
            "orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None, TimeSpan.FromSeconds(60));


        timeProvider.Advance(TimeSpan.FromSeconds(5));
        // The renewal loop runs in the background; give its tick a moment to complete.
        for (int attempt = 0; attempt < 50 && sessionA.ReceivedCalls().All(call => call.GetMethodInfo().Name != nameof(ServiceBusSessionReceiver.RenewSessionLockAsync)); attempt++)
        {
            await Task.Delay(20);
        }


        await sessionA.Received(1).RenewSessionLockAsync(Arg.Any<CancellationToken>());
        await sessionA.DidNotReceiveWithAnyArgs().RenewMessageLockAsync(default(ServiceBusReceivedMessage)!, default);
    }

    [Fact]
    public async Task PeekAsync_SessionFilter_ReturnsOnlyThatSessionWithoutSessionReceiver()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.PeekMessagesAsync(Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns([CreateMessage(1, "A"), CreateMessage(2, "B"), CreateMessage(3, "A")], []);
        sut.ReceiverFactory = (_, _, _, _) => receiver;
        int sessionAccepts = 0;
        sut.SessionReceiverFactory = (_, _, _, _) =>
        {
            sessionAccepts++;
            throw NoSessionAvailable;
        };


        IReadOnlyList<ToolboxMessage> peeked = await sut.PeekAsync(
            new PeekRequest("orders", MessageSubQueue.Active, 10, null, "A"),
            CancellationToken.None);


        peeked.Select(message => message.SequenceNumber).ShouldBe([1L, 3L]);
        sessionAccepts.ShouldBe(0);
    }

    [Fact]
    public async Task SendAsync_SessionLessSendRejected_ThrowsTypedErrorAndPrechecksNextSend()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusSender sender = Substitute.For<ServiceBusSender>();
        sender.CreateMessageBatchAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The SessionId was not set on a message, and it cannot be sent to the entity."));
        int senders = 0;
        sut.SenderFactory = _ =>
        {
            senders++;
            return sender;
        };
        ToolboxMessage message = new() { Body = "{}"u8.ToArray() };
        MessagingProviderException firstFailure = await Should.ThrowAsync<MessagingProviderException>(
            () => sut.SendAsync("orders", [message], CancellationToken.None));


        MessagingProviderException secondFailure = await Should.ThrowAsync<MessagingProviderException>(
            () => sut.SendAsync("orders", [message], CancellationToken.None));


        firstFailure.Message.ShouldContain("requires sessions");
        secondFailure.Message.ShouldContain("requires sessions");
        senders.ShouldBe(1);
    }

    [Fact]
    public async Task PurgeAsync_EntityRequiresSessions_DrainsEveryAvailableSession()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [[CreateMessage(1, "A")], []]);
        ServiceBusSessionReceiver sessionB = CreateSessionReceiver("B", [[CreateMessage(2, "B"), CreateMessage(3, "B")], []]);
        sut.SessionReceiverFactory = SessionSequence([], sessionA, sessionB);


        long deleted = await sut.PurgeAsync("orders", MessageSubQueue.Active, CancellationToken.None);


        deleted.ShouldBe(3);
        await sessionA.Received(1).DisposeAsync();
        await sessionB.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ListEntitiesAsync_SessionRequirementLearnedByReceive_FlagsModeledEntity()
    {
        IAzureServiceBusManagement management = Substitute.For<IAzureServiceBusManagement>();
        management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MessagingProviderUnavailableException("admin endpoint down"));
        IEmulatorRuntimeCountSampler sampler = Substitute.For<IEmulatorRuntimeCountSampler>();
        sampler.SampleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new EntityRuntimeInfo());
        await using AzureServiceBusProvider sut = new(
            EmulatorConnectionString,
            [new AspireModelDiscovery(["orders", "billing"], [])],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            management,
            sampler);
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        sut.SessionReceiverFactory = SessionSequence([]);
        await sut.ReceiveAsync(new ReceiveRequest("orders", MessageSubQueue.Active, 1, TimeSpan.FromSeconds(1)), CancellationToken.None);


        List<MessagingEntity> entities = await sut.ListEntitiesAsync(CancellationToken.None).ToListAsync();


        entities.Single(entity => entity.Path == "orders").RequiresSession.ShouldBeTrue();
        entities.Single(entity => entity.Path == "billing").RequiresSession.ShouldBeFalse();
    }

    private static AzureServiceBusProvider CreateProvider(TimeProvider? timeProvider = null)
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            // Pinned before SessionLockedUntil, so held session locks never count as expired.
            timeProvider ?? new FakeTimeProvider(SessionLockedUntil.AddMinutes(-1)),
            Substitute.For<IAzureServiceBusManagement>());

    private static SettleRequest CompleteRequest(params string[] lockTokens)
        => new("orders", MessageSubQueue.Active, SettlementOperation.Complete, lockTokens, null, null);

    private static ServiceBusReceivedMessage CreateMessage(long sequenceNumber, string sessionId, Guid lockToken = default)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData($"{{\"seq\": {sequenceNumber}}}"),
            messageId: $"msg-{sequenceNumber}",
            sessionId: sessionId,
            sequenceNumber: sequenceNumber,
            lockTokenGuid: lockToken,
            // Session messages carry a far-future lock of their own; the session lock is what counts.
            lockedUntil: DateTimeOffset.MaxValue);

    private static ServiceBusReceiver RejectingReceiver()
    {
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(NonSessionReceiveRejected);
        return receiver;
    }

    private static ServiceBusSessionReceiver CreateSessionReceiver(string sessionId, IReadOnlyList<IReadOnlyList<ServiceBusReceivedMessage>> batches)
    {
        ServiceBusSessionReceiver receiver = Substitute.For<ServiceBusSessionReceiver>();
        receiver.SessionId.Returns(sessionId);
        receiver.SessionLockedUntil.Returns(SessionLockedUntil);
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(batches[0], batches.Skip(1).ToArray());
        return receiver;
    }

    // Hands out the given session receivers in order, then reports that no session is available —
    // the way accept-next ends on the cloud (ServiceTimeout).
    private static Func<string, string?, ServiceBusSessionReceiverOptions, CancellationToken, Task<ServiceBusSessionReceiver>> SessionSequence(
        List<string?> requestedSessions,
        params ServiceBusSessionReceiver[] receivers)
    {
        Queue<ServiceBusSessionReceiver> pending = new(receivers);
        return (_, sessionId, _, _) =>
        {
            requestedSessions.Add(sessionId);
            return pending.TryDequeue(out ServiceBusSessionReceiver? receiver)
                ? Task.FromResult(receiver)
                : Task.FromException<ServiceBusSessionReceiver>(NoSessionAvailable);
        };
    }
}
