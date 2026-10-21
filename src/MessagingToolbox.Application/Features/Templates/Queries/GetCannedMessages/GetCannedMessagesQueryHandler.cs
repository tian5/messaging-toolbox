using Mediator;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagingToolbox.Application.Features.Templates.Queries.GetCannedMessages;

public sealed class GetCannedMessagesQueryHandler : IRequestHandler<GetCannedMessagesQuery, IReadOnlyList<CannedMessageDto>>
{
    private readonly IToolboxDbContextFactory _dbContextFactory;

    public GetCannedMessagesQueryHandler(IToolboxDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async ValueTask<IReadOnlyList<CannedMessageDto>> Handle(GetCannedMessagesQuery request, CancellationToken cancellationToken)
    {
        await using IToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        List<CannedMessage> messages = await dbContext.CannedMessages
            .AsNoTracking()
            .OrderBy(message => message.Folder)
            .ThenBy(message => message.Name)
            .ToListAsync(cancellationToken);

        return messages.Select(ToDto).ToList();
    }

    private static CannedMessageDto ToDto(CannedMessage message)
    {
        (Dictionary<string, string> properties, Dictionary<string, PropertyValueType> types) = CannedPropertiesJson.Deserialize(message.PropertiesJson);
        return new CannedMessageDto(message.Id, message.Name, message.Folder, message.Body, message.ContentType, properties, types);
    }
}
