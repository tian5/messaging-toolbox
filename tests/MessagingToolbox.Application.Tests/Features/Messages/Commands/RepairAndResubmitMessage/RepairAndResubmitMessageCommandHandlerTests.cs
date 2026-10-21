using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Messages.Commands.RepairAndResubmitMessage;
using MessagingToolbox.Application.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.RepairAndResubmitMessage;

public class RepairAndResubmitMessageCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IMessageTemplateEngine _templateEngine;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly RepairAndResubmitMessageCommandHandler _sut;

    public RepairAndResubmitMessageCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _templateEngine = Substitute.For<IMessageTemplateEngine>();
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _templateEngine.Render(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _provider.PrepareResubmitAsync(
                Arg.Any<string>(),
                Arg.Any<MessageSubQueue>(),
                Arg.Any<ToolboxMessage>(),
                Arg.Any<ResubmitOverrides>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ToolboxMessage>());
        _sut = new RepairAndResubmitMessageCommandHandler(_providerFactory, _templateEngine, _connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_WithRemoveOriginal_SendsCopyThenSettlesByTokenAndAudits()
    {
        _connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });

        _provider.SettleAsync(
                Arg.Is<SettleRequest>(request =>
                    request.EntityPath == "orders" &&
                    request.SubQueue == MessageSubQueue.DeadLetter &&
                    request.Operation == SettlementOperation.Complete &&
                    request.LockTokens.SequenceEqual(new[] { "original-token" })),
                Arg.Any<CancellationToken>())
            .Returns(["original-token"]);

        MessageDto original = new()
        {
            Body = "original body",
            BodyFormat = MessageBodyFormat.Text,
            BodySizeBytes = 13,
            SequenceNumber = 7,
            LockToken = "original-token",
        };

        RepairAndResubmitMessageCommand command = new(
            ConnectionId, "orders", "fixed body", "application/json", new Dictionary<string, string>(),
            "repaired", "corr-1", null,
            original,
            "orders", MessageSubQueue.DeadLetter, "original-token", 7, RemoveOriginalAfterSend: true);


        await _sut.Handle(command, CancellationToken.None);


        await _provider.Received(1).SendAsync("orders", Arg.Any<IReadOnlyList<ToolboxMessage>>(), Arg.Any<CancellationToken>());
        await _provider.Received(1).SettleAsync(
            Arg.Is<SettleRequest>(request =>
                request.Operation == SettlementOperation.Complete &&
                request.LockTokens.SequenceEqual(new[] { "original-token" })),
            Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.RepairResubmitRemove,
            "messaging",
            "orders",
            MessageSubQueue.DeadLetter,
            Arg.Is<string?>(details => details!.Contains("sequence: 7")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutRemoveOriginal_NeverSettles()
    {
        MessageDto original = new()
        {
            Body = "original body",
            BodyFormat = MessageBodyFormat.Text,
            BodySizeBytes = 14,
            SequenceNumber = 7,
            LockToken = "token",
        };

        RepairAndResubmitMessageCommand command = new(
            ConnectionId, "orders", "copy body", null, new Dictionary<string, string>(),
            null, null, null,
            original,
            "orders", MessageSubQueue.Active, "token", 7, RemoveOriginalAfterSend: false);


        await _sut.Handle(command, CancellationToken.None);


        await _provider.DidNotReceiveWithAnyArgs().SettleAsync(default!, default);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_PeekedOriginalWithoutLockToken_DoesNotSettleOrAudit()
    {
        MessageDto original = new()
        {
            Body = "original body",
            BodyFormat = MessageBodyFormat.Text,
            BodySizeBytes = 13,
            SequenceNumber = 42,
        };

        RepairAndResubmitMessageCommand command = new(
            ConnectionId, "orders", "fixed body", null, new Dictionary<string, string>(),
            null, null, null,
            original,
            "orders", MessageSubQueue.DeadLetter, null, 42, RemoveOriginalAfterSend: true);


        await _sut.Handle(command, CancellationToken.None);


        await _provider.DidNotReceiveWithAnyArgs().SettleAsync(default!, default);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_SendFails_DoesNotSettleOrAudit()
    {
        _provider
            .SendAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ToolboxMessage>>(), Arg.Any<CancellationToken>())
            .Throws(new MessagingProviderException("broker unavailable"));

        MessageDto original = new()
        {
            Body = "original body",
            BodyFormat = MessageBodyFormat.Text,
            BodySizeBytes = 13,
            SequenceNumber = 7,
            LockToken = "original-token",
        };

        RepairAndResubmitMessageCommand command = new(
            ConnectionId, "orders", "fixed body", null, new Dictionary<string, string>(),
            null, null, null,
            original,
            "orders", MessageSubQueue.DeadLetter, "original-token", 7, RemoveOriginalAfterSend: true);


        await Assert.ThrowsAsync<MessagingProviderException>(() => _sut.Handle(command, CancellationToken.None).AsTask());


        await _provider.DidNotReceiveWithAnyArgs().SettleAsync(default!, default);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
    }
}
