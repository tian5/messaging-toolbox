using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Management.Commands.DeleteEntity;
using MessagingToolbox.Application.Features.Management.Notifications;
using MessagingToolbox.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Commands.DeleteEntity;

public class DeleteEntityCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly IPublisher _publisher;
    private readonly DeleteEntityCommandHandler _sut;

    public DeleteEntityCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _auditLog = Substitute.For<IAuditLog>();
        _publisher = Substitute.For<IPublisher>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new DeleteEntityCommandHandler(
            _providerFactory,
            _connectionCatalog,
            _auditLog,
            _publisher,
            Substitute.For<ILogger<DeleteEntityCommandHandler>>());
    }

    [Fact]
    public async Task Handle_DeletesEntityAndAudits()
    {
        _connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId, Name = "messaging", DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus, ConnectionString = "cs", Source = ConnectionSource.Config,
            });


        await _sut.Handle(new DeleteEntityCommand(ConnectionId, "orders"), CancellationToken.None);


        await _provider.Received(1).DeleteEntityAsync("orders", Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.DeleteEntity,
            "messaging",
            "orders",
            MessageSubQueue.Active,
            "Entity deleted",
            Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<EntityListChangedNotification>(notification => notification.ConnectionId == ConnectionId),
            Arg.Any<CancellationToken>());
    }
}
