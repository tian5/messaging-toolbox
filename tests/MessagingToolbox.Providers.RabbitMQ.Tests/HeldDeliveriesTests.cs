using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class HeldDeliveriesTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly HeldDeliveries _sut;

    public HeldDeliveriesTests()
    {
        _channel.IsOpen.Returns(true);
        _sut = new HeldDeliveries(_timeProvider, NullLogger.Instance);
    }

    [Fact]
    public async Task ReceiveAsync_TwoDeliveries_ReturnsDistinctHeldTokens()
    {
        GivenDeliveries(1, 2);


        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();


        held.Count.ShouldBe(2);
        held[0].LockToken.ShouldEndWith(":1");
        held[1].LockToken.ShouldEndWith(":2");
        _sut.StillHeld([held[0].LockToken, held[1].LockToken, "unknown"]).Count.ShouldBe(2);
        await _channel.Received(3).BasicGetAsync("orders", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleAsync_CompleteAndDeadLetter_AcksAndRejectsWithoutRequeue()
    {
        GivenDeliveries(1, 2, 3);
        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();


        await _sut.SettleAsync([held[0].LockToken], SettlementOperation.Complete, CancellationToken.None);
        await _sut.SettleAsync([held[1].LockToken], SettlementOperation.DeadLetter, CancellationToken.None);
        await _sut.SettleAsync([held[2].LockToken], SettlementOperation.Abandon, CancellationToken.None);


        await _channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await _channel.Received(1).BasicRejectAsync(2, false, Arg.Any<CancellationToken>());
        await _channel.Received(1).BasicNackAsync(3, false, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleAsync_LastHeldDelivery_ClosesTheChannel()
    {
        GivenDeliveries(1);
        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();


        await _sut.SettleAsync([held[0].LockToken], SettlementOperation.Complete, CancellationToken.None);


        await _channel.Received(1).CloseAsync(Arg.Any<ushort>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        _sut.StillHeld([held[0].LockToken]).ShouldBeEmpty();
    }

    [Fact]
    public async Task SettleAsync_TokenOfReleasedChannel_ThrowsLockLost()
    {
        GivenDeliveries(1, 2);
        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();
        await _sut.DisposeAsync();


        await Should.ThrowAsync<MessageLockLostException>(
            () => _sut.SettleAsync([held[0].LockToken], SettlementOperation.Complete, CancellationToken.None));
    }

    [Fact]
    public async Task SettleAsync_Defer_ThrowsNotSupportedWithoutTouchingTheDelivery()
    {
        GivenDeliveries(1);
        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => _sut.SettleAsync([held[0].LockToken], SettlementOperation.Defer, CancellationToken.None));


        _sut.StillHeld([held[0].LockToken]).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseIdleAsync_IdlePastTimeout_NacksEverythingWithRequeueBeforeClosing()
    {
        GivenDeliveries(1, 2);
        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();


        _timeProvider.Advance(HeldDeliveries.IdleTimeout);
        await _sut.ReleaseIdleAsync();


        Received.InOrder(() =>
        {
            _channel.BasicNackAsync(2, true, true, Arg.Any<CancellationToken>());
            _channel.CloseAsync(Arg.Any<ushort>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        });
        _sut.StillHeld([held[0].LockToken]).ShouldBeEmpty();
    }

    [Fact]
    public async Task ReleaseIdleAsync_RecentlyUsed_KeepsDeliveriesHeld()
    {
        GivenDeliveries(1);
        IReadOnlyList<(string LockToken, BasicGetResult Delivery)> held = await ReceiveAsync();


        _timeProvider.Advance(HeldDeliveries.IdleTimeout - TimeSpan.FromSeconds(1));
        await _sut.ReleaseIdleAsync();


        _sut.StillHeld([held[0].LockToken]).Count.ShouldBe(1);
    }

    private Task<IReadOnlyList<(string LockToken, BasicGetResult Delivery)>> ReceiveAsync()
        => _sut.ReceiveAsync("orders", _ => Task.FromResult(_channel), 10, CancellationToken.None);

    private void GivenDeliveries(params ulong[] tags)
    {
        BasicGetResult?[] results = [.. tags.Select(tag => (BasicGetResult?)new BasicGetResult(
            tag, false, string.Empty, "orders", 0, new BasicProperties(), ReadOnlyMemory<byte>.Empty)), null];
        _channel.BasicGetAsync("orders", false, Arg.Any<CancellationToken>()).Returns(results[0], results[1..]);
    }
}
