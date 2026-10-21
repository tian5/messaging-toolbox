using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Commands.SettleBySequence;
using MessagingToolbox.Application.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.SettleBySequence;

public class SettleBySequenceCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly SettleBySequenceCommandHandler _sut;

    public SettleBySequenceCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _sut = new SettleBySequenceCommandHandler(_providerFactory, _connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_CompleteOperation_SettlesBySequenceAndWritesAudit()
    {
        _provider.SettleBySequenceNumbersAsync(Arg.Any<SettleBySequenceRequest>(), Arg.Any<CancellationToken>())
            .Returns([11, 12]);


        int settled = await _sut.Handle(
            new SettleBySequenceCommand(ConnectionId, "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [11, 12, 13]),
            CancellationToken.None);


        settled.ShouldBe(2);
        await _provider.Received(1).SettleBySequenceNumbersAsync(
            Arg.Is<SettleBySequenceRequest>(request =>
                request.EntityPath == "orders/$deadletter" &&
                request.SubQueue == MessageSubQueue.DeadLetter &&
                request.Operation == SettlementOperation.Complete &&
                request.SequenceNumbers.SequenceEqual(new[] { 11L, 12L, 13L })),
            Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.SettleComplete,
            Arg.Any<string>(),
            "orders/$deadletter",
            MessageSubQueue.DeadLetter,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AbandonOperation_DoesNotWriteAudit()
    {
        _provider.SettleBySequenceNumbersAsync(Arg.Any<SettleBySequenceRequest>(), Arg.Any<CancellationToken>())
            .Returns([11]);


        await _sut.Handle(
            new SettleBySequenceCommand(ConnectionId, "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Abandon, [11]),
            CancellationToken.None);


        await _auditLog.DidNotReceive().WriteAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageSubQueue>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoSequencesSettled_WritesNoAudit()
    {
        _provider.SettleBySequenceNumbersAsync(Arg.Any<SettleBySequenceRequest>(), Arg.Any<CancellationToken>())
            .Returns([]);


        int settled = await _sut.Handle(
            new SettleBySequenceCommand(ConnectionId, "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [99]),
            CancellationToken.None);


        settled.ShouldBe(0);
        await _auditLog.DidNotReceive().WriteAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageSubQueue>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
