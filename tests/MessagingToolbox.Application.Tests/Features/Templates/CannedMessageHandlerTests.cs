using Mediator;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Features.Templates.Commands.DeleteCannedMessage;
using MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;
using MessagingToolbox.Application.Features.Templates.Notifications;
using MessagingToolbox.Application.Features.Templates.Queries.GetCannedMessages;
using MessagingToolbox.Application.Tests.Common;
using MessagingToolbox.Domain.Common;
using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Templates;

public class CannedMessageHandlerTests : SqliteTestBase
{
    [Fact]
    public async Task Handle_MultipleMessages_ReturnsMappedAndOrderedByFolderThenName()
    {
        DbContext.CannedMessages.Add(new CannedMessage
        {
            Id = Guid.NewGuid(),
            Name = "zulu",
            Folder = "B",
            Body = "{}",
            ContentType = "application/json",
            PropertiesJson = "{\"k\":\"v\"}",
        });
        DbContext.CannedMessages.Add(new CannedMessage
        {
            Id = Guid.NewGuid(),
            Name = "alpha",
            Folder = "A",
            Body = "plain",
        });
        await DbContext.SaveChangesAsync(CancellationToken.None);

        GetCannedMessagesQueryHandler sut = new(DbContextFactory);


        IReadOnlyList<CannedMessageDto> result = await sut.Handle(new GetCannedMessagesQuery(), CancellationToken.None);


        result.Count.ShouldBe(2);
        result[0].Name.ShouldBe("alpha");
        result[0].Folder.ShouldBe("A");
        result[1].Name.ShouldBe("zulu");
        result[1].Folder.ShouldBe("B");
        result[1].Properties["k"].ShouldBe("v");
    }

    [Fact]
    public async Task Handle_NewMessage_SetsDefaultFolderPersistsAndPublishesNotification()
    {
        IPublisher publisher = Substitute.For<IPublisher>();
        ILogger<SaveCannedMessageCommandHandler> logger = Substitute.For<ILogger<SaveCannedMessageCommandHandler>>();
        SaveCannedMessageCommandHandler sut = new(DbContextFactory, publisher, logger);


        await sut.Handle(
            new SaveCannedMessageCommand(null, "Order template", " ", "{\"id\": 1}", "application/json", new Dictionary<string, string> { ["tenant"] = "tests" }),
            CancellationToken.None);


        CannedMessage persisted = DbContext.CannedMessages.Single();
        persisted.Name.ShouldBe("Order template");
        persisted.Folder.ShouldBe(AppConstants.DefaultCannedMessageFolder);
        persisted.Body.ShouldBe("{\"id\": 1}");
        persisted.ContentType.ShouldBe("application/json");
        persisted.PropertiesJson.ShouldContain("tenant");
        await publisher.Received(1).Publish(Arg.Any<CannedMessageChangedNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingMessage_UpdatesStoredEntity()
    {
        CannedMessage existing = new()
        {
            Id = Guid.NewGuid(),
            Name = "Old",
            Folder = "OldFolder",
            Body = "old-body",
            ContentType = "text/plain",
            PropertiesJson = "{}",
        };
        DbContext.CannedMessages.Add(existing);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        SaveCannedMessageCommandHandler sut = new(DbContextFactory, Substitute.For<IPublisher>(), Substitute.For<ILogger<SaveCannedMessageCommandHandler>>());


        await sut.Handle(
            new SaveCannedMessageCommand(existing.Id, "New", "Orders", "body", null, new Dictionary<string, string>()),
            CancellationToken.None);


        CannedMessage persisted = DbContext.CannedMessages.AsNoTracking().Single(message => message.Id == existing.Id);
        persisted.Name.ShouldBe("New");
        persisted.Folder.ShouldBe("Orders");
    }

    [Fact]
    public async Task Handle_ExistingMessage_DeletesAndPublishesNotification()
    {
        CannedMessage existing = new()
        {
            Id = Guid.NewGuid(),
            Name = "ToDelete",
            Folder = "Folder",
            Body = "body",
        };
        DbContext.CannedMessages.Add(existing);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        IPublisher publisher = Substitute.For<IPublisher>();
        DeleteCannedMessageCommandHandler sut = new(DbContextFactory, publisher, Substitute.For<ILogger<DeleteCannedMessageCommandHandler>>());


        await sut.Handle(new DeleteCannedMessageCommand(existing.Id), CancellationToken.None);


        DbContext.CannedMessages.ShouldBeEmpty();
        await publisher.Received(1).Publish(Arg.Any<CannedMessageChangedNotification>(), Arg.Any<CancellationToken>());
    }
}
