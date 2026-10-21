using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Audit.Queries.GetAuditEntries;
using MessagingToolbox.Application.Features.Templates.Queries.RenderTemplate;
using MessagingToolbox.Application.Tests.Common;
using MessagingToolbox.Domain.Entities;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Audit;

public class AuditHandlerTests : SqliteTestBase
{
    [Fact]
    public async Task Handle_MultipleEntries_ReturnsLatestFirstMappedToDtos()
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        AuditEntry older = new()
        {
            Id = Guid.NewGuid(),
            Action = "purge",
            ConnectionName = "cfg",
            EntityPath = "orders",
            SubQueue = Abstractions.Messaging.MessageSubQueue.Active,
        };
        DbContext.AuditEntries.Add(older);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        Time.SetUtcNow(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
        AuditEntry newer = new()
        {
            Id = Guid.NewGuid(),
            Action = "settle-complete",
            ConnectionName = "cfg",
            EntityPath = "orders",
            SubQueue = Abstractions.Messaging.MessageSubQueue.DeadLetter,
            Details = "details",
        };
        DbContext.AuditEntries.Add(newer);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        GetAuditEntriesQueryHandler sut = new(DbContextFactory);


        IReadOnlyList<AuditEntryDto> result = await sut.Handle(new GetAuditEntriesQuery(), CancellationToken.None);


        result.Count.ShouldBe(2);
        result[0].Action.ShouldBe("settle-complete");
        result[0].SubQueue.ShouldBe("DeadLetter");
        result[0].ConnectionName.ShouldBe("cfg");
        result[0].EntityPath.ShouldBe("orders");
        result[0].Details.ShouldBe("details");
        result[1].Action.ShouldBe("purge");
        result[1].SubQueue.ShouldBe("Active");
    }

    [Fact]
    public async Task Handle_TemplateRequest_ReturnsRenderedTemplate()
    {
        IMessageTemplateEngine engine = Substitute.For<IMessageTemplateEngine>();
        engine.Render("~newGuid~").Returns("rendered");
        RenderTemplateQueryHandler sut = new(engine);


        string result = await sut.Handle(new RenderTemplateQuery("~newGuid~"), CancellationToken.None);


        result.ShouldBe("rendered");
    }
}
