using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Messages.Commands.DeleteMessages;
using MessagingToolbox.Application.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.DeleteMessages;

public class DeleteMessagesCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly DeleteMessagesCommandHandler _sut;

    public DeleteMessagesCommandHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        IConnectionCatalog connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();

        _provider = Substitute.For<IMessagingProvider>();
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });

        _sut = new DeleteMessagesCommandHandler(providerFactory, connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_ReceivedActiveMessages_CompletesByLockTokenAndAudits()
    {
        _provider.SettleAsync(
                Arg.Is<SettleRequest>(request =>
                    request.SubQueue == MessageSubQueue.Active &&
                    request.Operation == SettlementOperation.Complete &&
                    request.LockTokens.SequenceEqual(new[] { "t1", "t2" })),
                Arg.Any<CancellationToken>())
            .Returns(["t1", "t2"]);


        int deleted = await _sut.Handle(
            new DeleteMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, [7, 9], ["t1", "t2"]),
            CancellationToken.None);


        deleted.ShouldBe(2);
        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.Complete &&
                request.LockTokens.SequenceEqual(new[] { "t1", "t2" })),
            Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().ReceiveAsync(default!, default);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.DeleteSelected,
            "messaging",
            "orders",
            MessageSubQueue.Active,
            Arg.Is<string?>(details => details!.Contains("2 of 2") && details.Contains("7") && details.Contains("9")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ScheduledSubQueue_CancelsSchedulesWithoutReceiving()
    {
        _provider.CancelScheduledAsync("orders", Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>())
            .Returns([7, 9]);


        int deleted = await _sut.Handle(
            new DeleteMessagesCommand(ConnectionId, "orders", MessageSubQueue.Scheduled, [7, 9]),
            CancellationToken.None);


        deleted.ShouldBe(2);
        await _provider.Received(1).CancelScheduledAsync(
            "orders",
            Arg.Is<IReadOnlyList<long>>(sequences => sequences.SequenceEqual(new long[] { 7, 9 })),
            Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().ReceiveAsync(default!, default);
    }

    [Fact]
    public async Task Handle_AllLocksExpired_ReturnsZeroAndDoesNotAudit()
    {
        _provider.SettleAsync(Arg.Any<SettleRequest>(), Arg.Any<CancellationToken>()).Returns([]);


        int deleted = await _sut.Handle(
            new DeleteMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, [7], ["t1"]),
            CancellationToken.None);


        deleted.ShouldBe(0);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_NoTokensForActiveSubQueue_DoesNotCallProvider()
    {
        int deleted = await _sut.Handle(
            new DeleteMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, [7], []),
            CancellationToken.None);


        deleted.ShouldBe(0);
        await _provider.DidNotReceiveWithAnyArgs().SettleAsync(default!, default);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }
}
