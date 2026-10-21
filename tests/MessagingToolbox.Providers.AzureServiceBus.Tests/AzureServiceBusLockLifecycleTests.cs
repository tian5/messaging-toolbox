using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusLockLifecycleTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetHeldLockStatesAsync_LockExpiredBeyondGrace_IsNoLongerHeld()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceivedMessage message = CreateLockedMessage(1, _timeProvider.GetUtcNow().AddSeconds(30));
        sut.ReceiverFactory = (_, _, _, _) => CreateReceiver([message]);
        await sut.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None);


        _timeProvider.Advance(TimeSpan.FromMinutes(3));
        IReadOnlyDictionary<string, DateTimeOffset> states = await sut.GetHeldLockStatesAsync(
            "orders", MessageSubQueue.Active, [message.LockToken], CancellationToken.None);


        states.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetHeldLockStatesAsync_LockJustExpired_StaysHeldWithinClockSkewGrace()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceivedMessage message = CreateLockedMessage(1, _timeProvider.GetUtcNow().AddSeconds(30));
        sut.ReceiverFactory = (_, _, _, _) => CreateReceiver([message]);
        await sut.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None);


        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        IReadOnlyDictionary<string, DateTimeOffset> states = await sut.GetHeldLockStatesAsync(
            "orders", MessageSubQueue.Active, [message.LockToken], CancellationToken.None);


        states.ShouldContainKey(message.LockToken);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        AzureServiceBusProvider sut = CreateProvider();
        await sut.DisposeAsync();


        await Should.NotThrowAsync(() => sut.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DisposeAsync_AfterConcurrentRenewingReceivesOnTwoEntities_CompletesCleanly()
    {
        AzureServiceBusProvider sut = CreateProvider();
        sut.ReceiverFactory = (path, _, _, _) => CreateReceiver([CreateLockedMessage(path == "orders" ? 1 : 2, _timeProvider.GetUtcNow().AddSeconds(30))]);
        await Task.WhenAll(
            sut.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None, TimeSpan.FromMinutes(1)),
            sut.ReceiveForSettlementAsync("billing", MessageSubQueue.Active, 10, TimeSpan.FromSeconds(1), CancellationToken.None, TimeSpan.FromMinutes(1)));


        _timeProvider.Advance(TimeSpan.FromSeconds(10));


        await Should.NotThrowAsync(() => sut.DisposeAsync().AsTask());
    }

    private AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            _timeProvider,
            Substitute.For<IAzureServiceBusManagement>());

    private static ServiceBusReceivedMessage CreateLockedMessage(long sequenceNumber, DateTimeOffset lockedUntil)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("{}"),
            messageId: $"msg-{sequenceNumber}",
            sequenceNumber: sequenceNumber,
            lockTokenGuid: Guid.NewGuid(),
            lockedUntil: lockedUntil);

    private static ServiceBusReceiver CreateReceiver(IReadOnlyList<ServiceBusReceivedMessage> messages)
    {
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(messages, []);
        return receiver;
    }
}
