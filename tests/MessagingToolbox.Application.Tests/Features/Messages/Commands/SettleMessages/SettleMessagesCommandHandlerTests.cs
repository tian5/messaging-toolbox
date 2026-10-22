using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.SettleMessages;

public class SettleMessagesCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly SettleMessagesCommandHandler _sut;

    public SettleMessagesCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _provider.Capabilities.Returns(TestCapabilities.Full);
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _provider.SettleAsync(Arg.Any<SettleRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SettleRequest>().LockTokens);
        _sut = new SettleMessagesCommandHandler(_providerFactory, _connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_CompleteOperation_SettlesByTokenAndAudits()
    {
        _connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });


        await _sut.Handle(
            new SettleMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, SettlementOperation.Complete, ["t1", "t2"], null, null),
            CancellationToken.None);


        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.Complete &&
                request.LockTokens.SequenceEqual(new[] { "t1", "t2" })),
            Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.SettleComplete,
            "messaging",
            "orders",
            MessageSubQueue.Active,
            Arg.Is<string?>(details => details!.Contains("2 message(s)")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AbandonOperation_SettlesWithoutAudit()
    {
        await _sut.Handle(
            new SettleMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, SettlementOperation.Abandon, ["t1"], null, null),
            CancellationToken.None);


        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.Abandon &&
                request.LockTokens.SequenceEqual(new[] { "t1" })),
            Arg.Any<CancellationToken>());
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_DeadLetterOperation_PassesReasonAndDescription()
    {
        await _sut.Handle(
            new SettleMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, SettlementOperation.DeadLetter, ["t1"], "Poison", "Cannot parse"),
            CancellationToken.None);


        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.DeadLetter &&
                request.Reason == "Poison" &&
                request.Description == "Cannot parse"),
            Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.SettleDeadLetter,
            Arg.Any<string>(),
            "orders",
            MessageSubQueue.Active,
            Arg.Is<string?>(details => details!.Contains("Poison")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyTokenList_DoesNotSettle()
    {
        await _sut.Handle(
            new SettleMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, SettlementOperation.Complete, [], null, null),
            CancellationToken.None);


        await _provider.DidNotReceiveWithAnyArgs().SettleAsync(default!, default);
    }

    [Fact]
    public async Task Handle_DeferWithoutDeferralSupport_ThrowsWithoutSettling()
    {
        _provider.Capabilities.Returns(TestCapabilities.Full with { SupportsDeferral = false });


        await Should.ThrowAsync<NotSupportedByProviderException>(() => _sut.Handle(
            new SettleMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, SettlementOperation.Defer, ["token-1"], null, null),
            CancellationToken.None).AsTask());


        await _provider.DidNotReceiveWithAnyArgs().SettleAsync(default!, default);
    }
}
