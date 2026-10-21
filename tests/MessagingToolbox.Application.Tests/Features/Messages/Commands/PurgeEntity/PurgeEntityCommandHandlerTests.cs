using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Messages.Commands.PurgeEntity;
using MessagingToolbox.Application.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.PurgeEntity;

public class PurgeEntityCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly PurgeEntityCommandHandler _sut;

    public PurgeEntityCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _auditLog = Substitute.For<IAuditLog>();
        _sut = new PurgeEntityCommandHandler(_providerFactory, _connectionCatalog, _auditLog);
    }

    [Fact]
    public async Task Handle_DeletedCount_ReturnsCountAndAudits()
    {
        _provider.PurgeAsync("orders", MessageSubQueue.DeadLetter, Arg.Any<CancellationToken>()).Returns(42L);
        _connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });


        long deleted = await _sut.Handle(new PurgeEntityCommand(ConnectionId, "orders", MessageSubQueue.DeadLetter), CancellationToken.None);


        deleted.ShouldBe(42);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.Purge,
            "messaging",
            "orders",
            MessageSubQueue.DeadLetter,
            Arg.Is<string?>(details => details!.Contains("42")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroDeleted_ReturnsZeroAndDoesNotAudit()
    {
        _provider.PurgeAsync("orders", MessageSubQueue.Active, Arg.Any<CancellationToken>()).Returns(0L);


        long deleted = await _sut.Handle(new PurgeEntityCommand(ConnectionId, "orders", MessageSubQueue.Active), CancellationToken.None);


        deleted.ShouldBe(0);
        await _auditLog.DidNotReceive().WriteAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageSubQueue>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
