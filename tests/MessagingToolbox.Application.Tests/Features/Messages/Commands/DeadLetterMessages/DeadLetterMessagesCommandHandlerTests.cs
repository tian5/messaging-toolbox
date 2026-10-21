using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;
using MessagingToolbox.Application.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.DeadLetterMessages;

public class DeadLetterMessagesCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly DeadLetterMessagesCommandHandler _sut;

    public DeadLetterMessagesCommandHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        IConnectionCatalog connectionCatalog = Substitute.For<IConnectionCatalog>();
        _provider = Substitute.For<IMessagingProvider>();
        _auditLog = Substitute.For<IAuditLog>();

        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });

        _sut = new DeadLetterMessagesCommandHandler(providerFactory, connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_ReceivedActiveMessages_DeadLettersByLockTokenAndAudits()
    {
        _provider.SettleAsync(
                Arg.Is<SettleRequest>(request =>
                    request.Operation == SettlementOperation.DeadLetter &&
                    request.Reason == "broken" &&
                    request.LockTokens.SequenceEqual(new[] { "target-token" })),
                Arg.Any<CancellationToken>())
            .Returns(["target-token"]);


        int moved = await _sut.Handle(
            new DeadLetterMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, [7], ["target-token"], "broken"),
            CancellationToken.None);


        moved.ShouldBe(1);
        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.EntityPath == "orders" &&
                request.SubQueue == MessageSubQueue.Active &&
                request.Operation == SettlementOperation.DeadLetter &&
                request.LockTokens.SequenceEqual(new[] { "target-token" })),
            Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().ReceiveAsync(default!, default);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.DeadLetterSelected,
            "messaging",
            "orders",
            MessageSubQueue.Active,
            Arg.Is<string?>(details => details!.Contains("1 of 1") && details.Contains("7") && details.Contains("broken")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SomeLocksExpired_ReducesReportedCountWithoutFailing()
    {
        _provider.SettleAsync(
                Arg.Is<SettleRequest>(request =>
                    request.Operation == SettlementOperation.DeadLetter &&
                    request.LockTokens.SequenceEqual(new[] { "t1", "t2" })),
                Arg.Any<CancellationToken>())
            .Returns(["t1"]);


        int moved = await _sut.Handle(
            new DeadLetterMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, [7, 9], ["t1", "t2"]),
            CancellationToken.None);


        moved.ShouldBe(1);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.DeadLetterSelected,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageSubQueue>(),
            Arg.Is<string?>(details => details!.Contains("1 of 2")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ScheduledMessage_CancelsResendsAndDeadLettersMarkedCopies()
    {
        ToolboxMessage scheduled = new()
        {
            Body = [1],
            SequenceNumber = 7,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(1),
        };
        _provider.PeekAsync(Arg.Any<PeekRequest>(), Arg.Any<CancellationToken>()).Returns([scheduled]);
        _provider.CancelScheduledAsync("orders", Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns([7]);

        List<ToolboxMessage> sent = [];
        await _provider.SendAsync("orders", Arg.Do<IReadOnlyList<ToolboxMessage>>(messages => sent.AddRange(messages)), Arg.Any<CancellationToken>());

        // The marked copy lands among other active messages; the scan dead-letters the marker
        // and abandons the rest. The marker Guid is generated inside the handler, so the copy
        // is built lazily from what was actually sent.
        ToolboxMessage other = new() { Body = [9], SequenceNumber = 43, LockToken = "other-token" };
        _provider.ReceiveForSettlementAsync("orders", MessageSubQueue.Active, Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ =>
            {
                ToolboxMessage sentCopy = sent.ShouldHaveSingleItem();
                return new List<ToolboxMessage>
                {
                    sentCopy with { SequenceNumber = 42, LockToken = "copy-token", ScheduledEnqueueTime = null },
                    other,
                };
            });

        _provider.SettleAsync(
                Arg.Is<SettleRequest>(request =>
                    request.SubQueue == MessageSubQueue.Active &&
                    request.Operation == SettlementOperation.DeadLetter &&
                    request.LockTokens.SequenceEqual(new[] { "copy-token" })),
                Arg.Any<CancellationToken>())
            .Returns(["copy-token"]);
        _provider.SettleAsync(
                Arg.Is<SettleRequest>(request =>
                    request.SubQueue == MessageSubQueue.Active &&
                    request.Operation == SettlementOperation.Abandon &&
                    request.LockTokens.SequenceEqual(new[] { "other-token" })),
                Arg.Any<CancellationToken>())
            .Returns(["other-token"]);


        int moved = await _sut.Handle(
            new DeadLetterMessagesCommand(ConnectionId, "orders", MessageSubQueue.Scheduled, [7]),
            CancellationToken.None);


        moved.ShouldBe(1);
        await _provider.Received(1).CancelScheduledAsync(
            "orders",
            Arg.Is<IReadOnlyList<long>>(sequences => sequences.SequenceEqual(new long[] { 7 })),
            Arg.Any<CancellationToken>());
        sent.ShouldHaveSingleItem().ScheduledEnqueueTime.ShouldBeNull();
        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.DeadLetter &&
                request.LockTokens.SequenceEqual(new[] { "copy-token" })),
            Arg.Any<CancellationToken>());
        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.Abandon &&
                request.LockTokens.SequenceEqual(new[] { "other-token" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoTokensForActiveSubQueue_DoesNotMoveAnything()
    {
        _provider.SettleAsync(Arg.Any<SettleRequest>(), Arg.Any<CancellationToken>()).Returns([]);


        int moved = await _sut.Handle(
            new DeadLetterMessagesCommand(ConnectionId, "orders", MessageSubQueue.Active, [7], []),
            CancellationToken.None);


        moved.ShouldBe(0);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }
}
