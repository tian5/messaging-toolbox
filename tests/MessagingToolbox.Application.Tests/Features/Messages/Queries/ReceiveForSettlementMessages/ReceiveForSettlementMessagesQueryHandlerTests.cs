using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveForSettlementMessages;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.ReceiveForSettlementMessages;

public class ReceiveForSettlementMessagesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly ReceiveForSettlementMessagesQueryHandler _sut;

    public ReceiveForSettlementMessagesQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        IConnectionCatalog connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new ReceiveForSettlementMessagesQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_ReturnsMappedMessagesWithLockTokensWithoutAudit()
    {
        ToolboxMessage message = new() { Body = "body"u8.ToArray(), SequenceNumber = 7, LockToken = "token-1" };
        _provider.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns([message]);


        IReadOnlyList<MessageDto> result = await _sut.Handle(
            new ReceiveForSettlementMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, 10, null),
            CancellationToken.None);


        result.Count.ShouldBe(1);
        result[0].LockToken.ShouldBe("token-1");
        result[0].SequenceNumber.ShouldBe(7);
        await _provider.Received(1).ReceiveForSettlementAsync(
            "orders", MessageSubQueue.Active, 10, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>(), null);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_SessionId_ReceivesFromThatSession()
    {
        _provider.Capabilities.Returns(TestCapabilities.DataPlane);
        _provider.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, 10, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>(), "order-42")
            .Returns([new ToolboxMessage { Body = "body"u8.ToArray(), SequenceNumber = 7, LockToken = "token-1", SessionId = "order-42" }]);


        IReadOnlyList<MessageDto> result = await _sut.Handle(
            new ReceiveForSettlementMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, 10, null, SessionId: "order-42"),
            CancellationToken.None);


        result.ShouldHaveSingleItem().SessionId.ShouldBe("order-42");
    }

    [Fact]
    public async Task Handle_SessionIdWithoutSessionSupport_ThrowsNotSupported()
    {
        _provider.Capabilities.Returns(TestCapabilities.DataPlane with { SupportsSessions = false });


        await Should.ThrowAsync<NotSupportedByProviderException>(() => _sut.Handle(
            new ReceiveForSettlementMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, 10, null, SessionId: "order-42"),
            CancellationToken.None).AsTask());


        await _provider.DidNotReceiveWithAnyArgs().ReceiveForSettlementAsync(default!, default, default, default, default);
    }
}
