using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Audit.Notifications;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Services;

public class AuditLogTests : SqliteTestBase
{
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly ILogger<AuditLog> _logger = Substitute.For<ILogger<AuditLog>>();

    [Fact]
    public async Task WriteAsync_Entry_PersistsAndPublishesNotification()
    {
        AuditLog sut = new(DbContextFactory, _publisher, _logger);


        await sut.WriteAsync(AuditLog.Purge, "local", "orders", MessageSubQueue.DeadLetter, "3 message(s)", CancellationToken.None);


        AuditEntry entry = await DbContext.AuditEntries.SingleAsync();
        entry.Action.ShouldBe(AuditLog.Purge);
        entry.EntityPath.ShouldBe("orders");
        entry.SubQueue.ShouldBe(MessageSubQueue.DeadLetter);
        await _publisher.Received(1).Publish(Arg.Any<AuditEntryWrittenNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_StoreFails_DoesNotThrowOrPublish()
    {
        IToolboxDbContextFactory failingFactory = Substitute.For<IToolboxDbContextFactory>();
        failingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is locked"));
        AuditLog sut = new(failingFactory, _publisher, _logger);


        await Should.NotThrowAsync(() => sut.WriteAsync(AuditLog.Purge, "local", "orders", MessageSubQueue.Active, null, CancellationToken.None));


        await _publisher.DidNotReceive().Publish(Arg.Any<AuditEntryWrittenNotification>(), Arg.Any<CancellationToken>());
    }
}
