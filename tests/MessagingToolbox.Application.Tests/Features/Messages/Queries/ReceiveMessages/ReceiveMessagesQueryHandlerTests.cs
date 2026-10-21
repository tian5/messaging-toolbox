using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveMessages;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.ReceiveMessages;

public class ReceiveMessagesQueryHandlerTests
{
    private static readonly Guid _connectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly ReceiveMessagesQueryHandler _sut;

    public ReceiveMessagesQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _providerFactory.GetProviderAsync(_connectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new ReceiveMessagesQueryHandler(_providerFactory, _connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_Receive_ReturnsMappedMessagesAndAudits()
    {
        ToolboxMessage message = new() { Body = "body"u8.ToArray(), SequenceNumber = 7 };
        _provider.ReceiveAsync(Arg.Any<ReceiveRequest>(), Arg.Any<CancellationToken>()).Returns([message]);
        _connectionCatalog.GetByIdAsync(_connectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = _connectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });


        IReadOnlyList<MessageDto> result = await _sut.Handle(
            new ReceiveMessagesQuery(_connectionId, "orders", MessageSubQueue.DeadLetter, 10, null),
            CancellationToken.None);


        result.Count.ShouldBe(1);
        result[0].SequenceNumber.ShouldBe(7);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.ReceiveDelete,
            "messaging",
            "orders",
            MessageSubQueue.DeadLetter,
            Arg.Is<string?>(details => details!.Contains("1 message") && details.Contains("7")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoMessages_DoesNotAudit()
    {
        _provider.ReceiveAsync(Arg.Any<ReceiveRequest>(), Arg.Any<CancellationToken>()).Returns([]);


        await _sut.Handle(
            new ReceiveMessagesQuery(_connectionId, "orders", MessageSubQueue.Active, 10, null),
            CancellationToken.None);


        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(
            default!,
            default!,
            default!,
            default,
            default,
            default);
    }

    [Fact]
    public async Task Handle_SessionId_PassesSessionToProvider()
    {
        _provider.Capabilities.Returns(new ProviderCapabilities(
            CanManageEntities: false, CanManageRules: false, CanPeek: true, CanReceiveSettle: true,
            SupportsSessions: true, SupportsScheduling: true, SupportsTransactions: false, MaxMessageSizeBytes: 256 * 1024));
        _provider.ReceiveAsync(Arg.Any<ReceiveRequest>(), Arg.Any<CancellationToken>()).Returns([]);


        await _sut.Handle(
            new ReceiveMessagesQuery(_connectionId, "orders", MessageSubQueue.Active, 10, null, "order-42"),
            CancellationToken.None);


        await _provider.Received(1).ReceiveAsync(
            Arg.Is<ReceiveRequest>(request => request.SessionId == "order-42" && request.EntityPath == "orders"),
            Arg.Any<CancellationToken>());
    }
}
