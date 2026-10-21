using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Connections.Commands.DeleteConnection;
using MessagingToolbox.Application.Features.Connections.Notifications;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using MessagingToolbox.Domain.Entities;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Connections.Commands.DeleteConnection;

public class DeleteConnectionCommandHandlerTests : SqliteTestBase
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IAuditLog _auditLog;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteConnectionCommandHandler> _logger;

    public DeleteConnectionCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _auditLog = Substitute.For<IAuditLog>();
        _publisher = Substitute.For<IPublisher>();
        _logger = Substitute.For<ILogger<DeleteConnectionCommandHandler>>();
    }

    [Fact]
    public async Task Handle_ExistingProfile_RemovesAndEvicts()
    {
        ConnectionProfile existing = new() { Id = Guid.NewGuid(), DisplayName = "ToDelete" };
        DbContext.ConnectionProfiles.Add(existing);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        DeleteConnectionCommandHandler sut = new(DbContextFactory, _providerFactory, _auditLog, _publisher, _logger);


        await sut.Handle(new DeleteConnectionCommand(existing.Id), CancellationToken.None);


        DbContext.ConnectionProfiles.ShouldBeEmpty();
        await _providerFactory.Received(1).DisposeAsync(existing.Id);
        await _auditLog.Received(1).WriteAsync(AuditLog.DeleteConnection, "ToDelete", string.Empty, MessageSubQueue.Active, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(Arg.Any<SavedConnectionChangedNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingProfile_EvictsProviderAndLeavesDatabaseEmpty()
    {
        Guid id = Guid.NewGuid();

        DeleteConnectionCommandHandler sut = new(DbContextFactory, _providerFactory, _auditLog, _publisher, _logger);


        await sut.Handle(new DeleteConnectionCommand(id), CancellationToken.None);


        DbContext.ConnectionProfiles.ShouldBeEmpty();
        await _providerFactory.Received(1).DisposeAsync(id);
        await _auditLog.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default, default, default);
        await _publisher.Received(1).Publish(Arg.Any<SavedConnectionChangedNotification>(), Arg.Any<CancellationToken>());
    }
}
