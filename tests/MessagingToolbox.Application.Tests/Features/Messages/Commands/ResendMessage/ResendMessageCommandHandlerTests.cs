using System.Text;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Commands.ResendMessage;
using MessagingToolbox.Application.Services;
using NSubstitute;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.ResendMessage;

public class ResendMessageCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly ResendMessageCommandHandler _sut;

    public ResendMessageCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _sut = new ResendMessageCommandHandler(_providerFactory, _connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_Message_ClonesWithoutSettlementMetadata()
    {
        MessageDto original = new()
        {
            Body = "{\"orderId\": 7}",
            BodyFormat = MessageBodyFormat.Json,
            BodySizeBytes = 13,
            MessageId = "msg-1",
            Subject = "order-created",
            CorrelationId = "corr-1",
            SessionId = "session-1",
            ContentType = "application/json",
            SequenceNumber = 99,
            LockToken = "original-token",
            ApplicationProperties = new Dictionary<string, object?> { ["tenant"] = "tests" },
        };


        _provider.PrepareResubmitAsync(
                Arg.Any<string>(),
                Arg.Any<MessageSubQueue>(),
                Arg.Any<ToolboxMessage>(),
                Arg.Any<ResubmitOverrides>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ToolboxMessage>());

        await _sut.Handle(new ResendMessageCommand(ConnectionId, "retry-queue", "source-queue", MessageSubQueue.Active, original), CancellationToken.None);


        await _provider.Received(1).PrepareResubmitAsync(
            "source-queue",
            MessageSubQueue.Active,
            Arg.Any<ToolboxMessage>(),
            ResubmitOverrides.Empty,
            Arg.Any<CancellationToken>());
        await _provider.Received(1).SendAsync(
            "retry-queue",
            Arg.Is<IReadOnlyList<ToolboxMessage>>(messages =>
                messages.Count == 1 &&
                Encoding.UTF8.GetString(messages[0].Body) == "{\"orderId\": 7}" &&
                messages[0].MessageId == "msg-1" &&
                messages[0].Subject == "order-created" &&
                messages[0].CorrelationId == "corr-1" &&
                messages[0].SessionId == "session-1" &&
                messages[0].ContentType == "application/json" &&
                (string?)messages[0].ApplicationProperties["tenant"] == "tests" &&
                messages[0].SequenceNumber == null &&
                messages[0].LockToken == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Message_WritesAuditEntry()
    {
        MessageDto original = new()
        {
            Body = "{\"orderId\": 7}",
            BodyFormat = MessageBodyFormat.Json,
            BodySizeBytes = 13,
            MessageId = "msg-1",
            SequenceNumber = 99,
        };

        _provider.PrepareResubmitAsync(
                Arg.Any<string>(),
                Arg.Any<MessageSubQueue>(),
                Arg.Any<ToolboxMessage>(),
                Arg.Any<ResubmitOverrides>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ToolboxMessage>());


        await _sut.Handle(new ResendMessageCommand(ConnectionId, "retry-queue", "source-queue", MessageSubQueue.Active, original), CancellationToken.None);


        await _auditLog.Received(1).WriteAsync(
            AuditLog.Resend,
            Arg.Any<string>(),
            "source-queue",
            MessageSubQueue.Active,
            Arg.Is<string>(details => details.Contains("retry-queue", StringComparison.Ordinal) && details.Contains("99", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
}
