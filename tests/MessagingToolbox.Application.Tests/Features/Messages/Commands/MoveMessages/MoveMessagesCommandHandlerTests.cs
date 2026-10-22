using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Commands.MoveMessages;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.MoveMessages;

public class MoveMessagesCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly MoveMessagesCommandHandler _sut;

    public MoveMessagesCommandHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _auditLog = Substitute.For<IAuditLog>();
        _provider.Capabilities.Returns(TestCapabilities.Full);
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new MoveMessagesCommandHandler(
            providerFactory,
            Substitute.For<IConnectionCatalog>(),
            _auditLog);
    }

    [Fact]
    public async Task Handle_MessagesMoved_ReturnsCountAndAudits()
    {
        _provider.MoveMessagesAsync("queues/orders.dlq", "queues/orders", Arg.Any<CancellationToken>()).Returns(3);


        long moved = await _sut.Handle(new MoveMessagesCommand(ConnectionId, "queues/orders.dlq", "queues/orders"), CancellationToken.None);


        moved.ShouldBe(3);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.Move, Arg.Any<string>(), "queues/orders.dlq", MessageSubQueue.Active, "3 message(s) moved to queues/orders", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NothingMoved_DoesNotAudit()
    {
        long moved = await _sut.Handle(new MoveMessagesCommand(ConnectionId, "queues/orders.dlq", "queues/orders"), CancellationToken.None);


        moved.ShouldBe(0);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_MovingNotSupported_ThrowsWithoutCallingTheBroker()
    {
        _provider.Capabilities.Returns(TestCapabilities.Full with { CanMoveMessages = false });


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => _sut.Handle(new MoveMessagesCommand(ConnectionId, "queues/orders.dlq", "queues/orders"), CancellationToken.None).AsTask());


        await _provider.DidNotReceiveWithAnyArgs().MoveMessagesAsync(default!, default!, default);
    }
}
