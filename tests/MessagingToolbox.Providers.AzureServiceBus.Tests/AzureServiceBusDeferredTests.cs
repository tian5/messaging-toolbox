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

public class AzureServiceBusDeferredTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private static readonly DateTimeOffset SessionLockedUntil = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    // The broker rejects a deferred batch as a whole when one sequence number can't be locked.
    private static readonly ServiceBusException NotAvailable =
        new("Failed to lock one or more specified messages.", ServiceBusFailureReason.MessageNotFound);

    // What the emulator reports when a plain receiver asks for a session entity's deferred messages.
    private static readonly ServiceBusException PlainReceiverOnSessionEntity =
        new("The session lock has expired on the MessageSession.", ServiceBusFailureReason.SessionLockLost);

    private static readonly ServiceBusException NoSessionAvailable = new("No session available.", ServiceBusFailureReason.ServiceTimeout);

    [Fact]
    public async Task ReceiveDeferredForSettlementAsync_PlainEntity_HoldsLocksForSettlement()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceivedMessage deferred = CreateMessage(7, lockToken: Guid.NewGuid());
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveDeferredMessagesAsync(Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>()).Returns([deferred]);
        sut.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest("orders", MessageSubQueue.Active, [new DeferredMessageReference(7)]),
            CancellationToken.None);
        IReadOnlyList<string> settled = await sut.SettleAsync(CompleteRequest(deferred.LockToken), CancellationToken.None);


        received.ShouldHaveSingleItem().LockToken.ShouldBe(deferred.LockToken);
        settled.ShouldBe([deferred.LockToken]);
        await receiver.Received(1).CompleteMessageAsync(deferred, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveDeferredForSettlementAsync_BatchRejected_RetriesOneByOneAndSkipsUnavailable()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceivedMessage available = CreateMessage(7, lockToken: Guid.NewGuid());
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveDeferredMessagesAsync(Arg.Is<IEnumerable<long>>(sequences => sequences.Count() > 1), Arg.Any<CancellationToken>())
            .ThrowsAsync(NotAvailable);
        receiver.ReceiveDeferredMessagesAsync(Arg.Is<IEnumerable<long>>(sequences => sequences.SequenceEqual(new[] { 7L })), Arg.Any<CancellationToken>())
            .Returns([available]);
        receiver.ReceiveDeferredMessagesAsync(Arg.Is<IEnumerable<long>>(sequences => sequences.SequenceEqual(new[] { 8L })), Arg.Any<CancellationToken>())
            .ThrowsAsync(NotAvailable);
        sut.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest("orders", MessageSubQueue.Active, [new DeferredMessageReference(7), new DeferredMessageReference(8)]),
            CancellationToken.None);


        received.ShouldHaveSingleItem().SequenceNumber.ShouldBe(7);
    }

    [Fact]
    public async Task ReceiveDeferredForSettlementAsync_SessionEntity_ReceivesThroughEachMessagesSessionAndHoldsIt()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusReceivedMessage fromA = CreateMessage(1, "A", Guid.NewGuid());
        ServiceBusReceivedMessage fromB = CreateMessage(2, "B", Guid.NewGuid());
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [fromA]);
        ServiceBusSessionReceiver sessionB = CreateSessionReceiver("B", [fromB]);
        List<string?> requestedSessions = [];
        sut.SessionReceiverFactory = SessionsById(requestedSessions, sessionA, sessionB);


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest("orders", MessageSubQueue.Active, [new DeferredMessageReference(1, "A"), new DeferredMessageReference(2, "B")]),
            CancellationToken.None);
        await sut.SettleAsync(CompleteRequest(fromA.LockToken), CancellationToken.None);


        received.Select(message => message.SessionId).ShouldBe(["A", "B"]);
        received.ShouldAllBe(message => message.LockedUntil == SessionLockedUntil);
        requestedSessions.ShouldBe(["A", "B"]);
        await sessionA.Received(1).CompleteMessageAsync(fromA, Arg.Any<CancellationToken>());
        await sessionA.Received(1).DisposeAsync();
        await sessionB.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task ReceiveDeferredForSettlementAsync_SessionLockedElsewhere_SkipsThatSession()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusSessionReceiver sessionB = CreateSessionReceiver("B", [CreateMessage(2, "B", Guid.NewGuid())]);
        sut.SessionReceiverFactory = (_, sessionId, _, _) => sessionId == "B"
            ? Task.FromResult(sessionB)
            : Task.FromException<ServiceBusSessionReceiver>(new ServiceBusException("Locked.", ServiceBusFailureReason.SessionCannotBeLocked));


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest("orders", MessageSubQueue.Active, [new DeferredMessageReference(1, "A"), new DeferredMessageReference(2, "B")]),
            CancellationToken.None);


        received.ShouldHaveSingleItem().SessionId.ShouldBe("B");
    }

    [Fact]
    public async Task ReceiveDeferredForSettlementAsync_NothingReceivedFromSession_ReleasesIt()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (_, _, _, _) => RejectingReceiver();
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", []);
        sut.SessionReceiverFactory = SessionsById([], sessionA);


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest("orders", MessageSubQueue.Active, [new DeferredMessageReference(1, "A")]),
            CancellationToken.None);


        received.ShouldBeEmpty();
        await sessionA.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReceiveDeferredForSettlementAsync_ScheduledSubQueue_ThrowsNotSupported()
    {
        await using AzureServiceBusProvider sut = CreateProvider();


        await Should.ThrowAsync<NotSupportedByProviderException>(() => sut.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest("orders", MessageSubQueue.Scheduled, [new DeferredMessageReference(1)]),
            CancellationToken.None));
    }

    [Fact]
    public async Task PurgeAsync_DeferredMessagesLeftAfterDrain_DeletesThemBySequenceNumber()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceiver deleter = Substitute.For<ServiceBusReceiver>();
        deleter.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns([CreateMessage(1)], new List<ServiceBusReceivedMessage>());
        deleter.ReceiveDeferredMessagesAsync(Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>())
            .Returns([CreateMessage(5)]);
        ServiceBusReceiver peeker = PeekingReceiver([CreateMessage(5, state: ServiceBusMessageState.Deferred)]);
        sut.ReceiverFactory = (_, _, mode, _) => mode == ServiceBusReceiveMode.ReceiveAndDelete ? deleter : peeker;


        long deleted = await sut.PurgeAsync("orders", MessageSubQueue.Active, CancellationToken.None);


        deleted.ShouldBe(2);
        await deleter.Received(1).ReceiveDeferredMessagesAsync(
            Arg.Is<IEnumerable<long>>(sequences => sequences.SequenceEqual(new[] { 5L })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PurgeAsync_DeferredSessionMessages_DeletesThemThroughTheirSession()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceiver peeker = PeekingReceiver([CreateMessage(5, "A", state: ServiceBusMessageState.Deferred)]);
        sut.ReceiverFactory = (_, _, mode, _) => mode == ServiceBusReceiveMode.ReceiveAndDelete ? RejectingReceiver() : peeker;
        ServiceBusSessionReceiver sessionA = CreateSessionReceiver("A", [CreateMessage(5, "A")]);
        List<string?> requestedSessions = [];
        sut.SessionReceiverFactory = SessionsById(requestedSessions, sessionA);


        long deleted = await sut.PurgeAsync("orders", MessageSubQueue.Active, CancellationToken.None);


        deleted.ShouldBe(1);
        requestedSessions.ShouldBe([null, "A"]);
        await sessionA.Received(1).DisposeAsync();
    }

    private static AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            // Pinned before SessionLockedUntil, so held session locks never count as expired.
            new FakeTimeProvider(SessionLockedUntil.AddMinutes(-1)),
            Substitute.For<IAzureServiceBusManagement>());

    private static SettleRequest CompleteRequest(params string[] lockTokens)
        => new("orders", MessageSubQueue.Active, SettlementOperation.Complete, lockTokens, null, null);

    private static ServiceBusReceivedMessage CreateMessage(
        long sequenceNumber,
        string? sessionId = null,
        Guid lockToken = default,
        ServiceBusMessageState state = ServiceBusMessageState.Active)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData($"{{\"seq\": {sequenceNumber}}}"),
            messageId: $"msg-{sequenceNumber}",
            sessionId: sessionId,
            sequenceNumber: sequenceNumber,
            lockTokenGuid: lockToken,
            lockedUntil: DateTimeOffset.MaxValue,
            serviceBusMessageState: state);

    // Rejects plain receives on a session entity the way the broker does.
    private static ServiceBusReceiver RejectingReceiver()
    {
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("It is not possible for an entity that requires sessions to create a non-sessionful message receiver."));
        receiver.ReceiveDeferredMessagesAsync(Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(PlainReceiverOnSessionEntity);
        return receiver;
    }

    // First page from the head, empty afterwards — an exhausted sub-queue.
    private static ServiceBusReceiver PeekingReceiver(IReadOnlyList<ServiceBusReceivedMessage> messages)
    {
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.PeekMessagesAsync(Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<long?>(1) is null ? messages : []);
        return receiver;
    }

    private static ServiceBusSessionReceiver CreateSessionReceiver(string sessionId, IReadOnlyList<ServiceBusReceivedMessage> deferred)
    {
        ServiceBusSessionReceiver receiver = Substitute.For<ServiceBusSessionReceiver>();
        receiver.SessionId.Returns(sessionId);
        receiver.SessionLockedUntil.Returns(SessionLockedUntil);
        receiver.ReceiveDeferredMessagesAsync(Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>()).Returns(deferred);
        return receiver;
    }

    // Hands out the receiver of the named session; accept-next finds no session available.
    private static Func<string, string?, ServiceBusSessionReceiverOptions, CancellationToken, Task<ServiceBusSessionReceiver>> SessionsById(
        List<string?> requestedSessions,
        params ServiceBusSessionReceiver[] receivers)
        => (_, sessionId, _, _) =>
        {
            requestedSessions.Add(sessionId);
            ServiceBusSessionReceiver? receiver = receivers.FirstOrDefault(candidate => candidate.SessionId == sessionId);
            return receiver is null
                ? Task.FromException<ServiceBusSessionReceiver>(NoSessionAvailable)
                : Task.FromResult(receiver);
        };
}
