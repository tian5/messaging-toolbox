using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.RequeueMessages;

public class RequeueMessagesCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly RequeueMessagesCommandHandler _sut;

    public RequeueMessagesCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _provider.Capabilities.Returns(TestCapabilities.Full);
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _sut = new RequeueMessagesCommandHandler(_providerFactory, _connectionCatalog, _auditLog);

        _provider.PrepareResubmitAsync(
                Arg.Any<string>(),
                Arg.Any<MessageSubQueue>(),
                Arg.Any<ToolboxMessage>(),
                Arg.Any<ResubmitOverrides>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ToolboxMessage>());

        _provider.SettleAsync(Arg.Any<SettleRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SettleRequest>().LockTokens);
    }

    [Fact]
    public async Task Handle_MultipleMessages_SendsClonesToChosenTarget()
    {
        IReadOnlyList<MessageDto> messages =
        [
            CreateMessage("msg-1", 11),
            CreateMessage("msg-2", 12),
        ];


        int sent = await _sut.Handle(
            new RequeueMessagesCommand(ConnectionId, "orders/$deadletter", MessageSubQueue.DeadLetter, "retry-queue", messages),
            CancellationToken.None);


        sent.ShouldBe(2);
        await _provider.Received(2).PrepareResubmitAsync(
            "orders/$deadletter",
            MessageSubQueue.DeadLetter,
            Arg.Any<ToolboxMessage>(),
            ResubmitOverrides.Empty,
            Arg.Any<CancellationToken>());
        await _provider.Received(1).SendAsync(
            "retry-queue",
            Arg.Is<IReadOnlyList<ToolboxMessage>>(clones =>
                clones.Count == 2 &&
                clones.All(clone => clone.LockToken == null && clone.SequenceNumber == null) &&
                clones.Select(clone => clone.MessageId).OrderBy(id => id).SequenceEqual(new[] { "msg-1", "msg-2" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemoveOriginalsTrue_SettlesOriginalsWithComplete()
    {
        IReadOnlyList<MessageDto> messages = [CreateMessage("msg-1", 11)];


        await _sut.Handle(
            new RequeueMessagesCommand(
                ConnectionId,
                "orders/$deadletter",
                MessageSubQueue.DeadLetter,
                "retry-queue",
                messages,
                LockTokens: ["token-1", "token-2"],
                RemoveOriginals: true),
            CancellationToken.None);


        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.EntityPath == "orders/$deadletter" &&
                request.SubQueue == MessageSubQueue.DeadLetter &&
                request.Operation == SettlementOperation.Complete &&
                request.LockTokens.SequenceEqual(new[] { "token-1", "token-2" })),
            Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.Requeue,
            Arg.Any<string>(),
            "orders/$deadletter",
            MessageSubQueue.DeadLetter,
            Arg.Is<string>(details => details.Contains("removed from the DLQ", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemoveOriginalsFalse_LeavesOriginalsAndWritesAudit()
    {
        IReadOnlyList<MessageDto> messages = [CreateMessage("msg-1", 11)];


        await _sut.Handle(
            new RequeueMessagesCommand(
                ConnectionId,
                "orders/$deadletter",
                MessageSubQueue.DeadLetter,
                "retry-queue",
                messages,
                LockTokens: ["token-1"],
                RemoveOriginals: false),
            CancellationToken.None);


        await _provider.DidNotReceive().SettleAsync(Arg.Any<SettleRequest>(), Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.Requeue,
            Arg.Any<string>(),
            "orders/$deadletter",
            MessageSubQueue.DeadLetter,
            Arg.Is<string>(details => details.Contains("originals kept", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemoveOriginalsWithoutLockTokens_SettlesBySequenceScan()
    {
        IReadOnlyList<MessageDto> messages = [CreateMessage("msg-1", 11)];
        _provider.SettleBySequenceNumbersAsync(Arg.Any<SettleBySequenceRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SettleBySequenceRequest>().SequenceNumbers);


        await _sut.Handle(
            new RequeueMessagesCommand(
                ConnectionId,
                "orders/$deadletter",
                MessageSubQueue.DeadLetter,
                "retry-queue",
                messages,
                RemoveOriginals: true),
            CancellationToken.None);


        await _provider.Received(1).SettleBySequenceNumbersAsync(
            Arg.Is<SettleBySequenceRequest>(request =>
                request.EntityPath == "orders/$deadletter" &&
                request.SubQueue == MessageSubQueue.DeadLetter &&
                request.Operation == SettlementOperation.Complete &&
                request.SequenceNumbers.SequenceEqual(new[] { 11L })),
            Arg.Any<CancellationToken>());
        await _provider.DidNotReceive().SettleAsync(Arg.Any<SettleRequest>(), Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.Requeue,
            Arg.Any<string>(),
            "orders/$deadletter",
            MessageSubQueue.DeadLetter,
            Arg.Is<string>(details => details.Contains("removed from the DLQ", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    private static MessageDto CreateMessage(string messageId, long sequenceNumber)
        => new()
        {
            Body = $"{{\"id\": \"{messageId}\"}}",
            BodyFormat = MessageBodyFormat.Json,
            BodySizeBytes = 16,
            MessageId = messageId,
            Subject = "order-created",
            SequenceNumber = sequenceNumber,
            LockToken = "original-token",
            ApplicationProperties = new Dictionary<string, object?> { ["tenant"] = "tests" },
        };

    [Fact]
    public async Task Handle_RemoveOriginalsWithoutLockTokensOrSequenceNumbers_ThrowsBeforeSendingClones()
    {
        _provider.Capabilities.Returns(TestCapabilities.Full with { SupportsSequenceNumbers = false });
        IReadOnlyList<MessageDto> messages = [CreateMessage("msg-1", 11)];


        await Should.ThrowAsync<NotSupportedByProviderException>(() => _sut.Handle(
            new RequeueMessagesCommand(ConnectionId, "orders.dlq", MessageSubQueue.Active, "orders", messages, RemoveOriginals: true),
            CancellationToken.None).AsTask());


        await _provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }
}
