using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.ReceiveDeferredMessages;

public class ReceiveDeferredMessagesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProvider _provider;
    private readonly ReceiveDeferredMessagesQueryHandler _sut;

    public ReceiveDeferredMessagesQueryHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new ReceiveDeferredMessagesQueryHandler(providerFactory);
    }

    [Fact]
    public async Task Handle_DeferredMessages_PassesReferencesAndRenewalAndReturnsHeldMessages()
    {
        _provider.Capabilities.Returns(Capabilities(canReceiveSettle: true));
        _provider.ReceiveDeferredForSettlementAsync(Arg.Any<ReceiveDeferredRequest>(), Arg.Any<CancellationToken>())
            .Returns([new ToolboxMessage { Body = "body"u8.ToArray(), SequenceNumber = 7, LockToken = "token-1", State = MessageState.Deferred, SessionId = "A" }]);


        IReadOnlyList<MessageDto> result = await _sut.Handle(
            new ReceiveDeferredMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, [new DeferredMessageReference(7, "A")], 60),
            CancellationToken.None);


        MessageDto message = result.ShouldHaveSingleItem();
        message.LockToken.ShouldBe("token-1");
        message.State.ShouldBe(MessageState.Deferred);
        await _provider.Received(1).ReceiveDeferredForSettlementAsync(
            Arg.Is<ReceiveDeferredRequest>(request => request.EntityPath == "orders"
                && request.SubQueue == MessageSubQueue.Active
                && request.Messages.Single() == new DeferredMessageReference(7, "A")
                && request.LockRenewalDuration == TimeSpan.FromSeconds(60)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroRenewal_RequestsNoRenewal()
    {
        _provider.Capabilities.Returns(Capabilities(canReceiveSettle: true));


        await _sut.Handle(
            new ReceiveDeferredMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, [new DeferredMessageReference(7)], 0),
            CancellationToken.None);


        await _provider.Received(1).ReceiveDeferredForSettlementAsync(
            Arg.Is<ReceiveDeferredRequest>(request => request.LockRenewalDuration == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConnectionCannotReceiveForSettlement_ThrowsNotSupported()
    {
        _provider.Capabilities.Returns(Capabilities(canReceiveSettle: false));


        await Should.ThrowAsync<NotSupportedByProviderException>(() => _sut.Handle(
            new ReceiveDeferredMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, [new DeferredMessageReference(7)]),
            CancellationToken.None).AsTask());
    }

    private static ProviderCapabilities Capabilities(bool canReceiveSettle) =>
        TestCapabilities.DataPlane with { CanReceiveSettle = canReceiveSettle };
}
