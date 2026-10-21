using Mediator;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Templates.Notifications;
using MessagingToolbox.Domain.Common;
using MessagingToolbox.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;

public sealed class SaveCannedMessageCommandHandler : IRequestHandler<SaveCannedMessageCommand>
{
    private readonly IToolboxDbContextFactory _dbContextFactory;
    private readonly IPublisher _publisher;
    private readonly ILogger<SaveCannedMessageCommandHandler> _logger;

    public SaveCannedMessageCommandHandler(
        IToolboxDbContextFactory dbContextFactory,
        IPublisher publisher,
        ILogger<SaveCannedMessageCommandHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(SaveCannedMessageCommand request, CancellationToken cancellationToken)
    {
        await using IToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        string folder = string.IsNullOrWhiteSpace(request.Folder) ? AppConstants.DefaultCannedMessageFolder : request.Folder;
        string propertiesJson = CannedPropertiesJson.Serialize(request.Properties, request.PropertyTypes);

        if (request.Id is { } id)
        {
            CannedMessage existing = await dbContext.CannedMessages.FindAsync([id], cancellationToken)
                ?? throw new ArgumentException($"Canned message '{id}' was not found.", nameof(request));
            existing.Name = request.Name;
            existing.Folder = folder;
            existing.Body = request.Body;
            existing.ContentType = request.ContentType;
            existing.PropertiesJson = propertiesJson;
        }
        else
        {
            CannedMessage newMessage = new()
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Folder = folder,
                Body = request.Body,
                ContentType = request.ContentType,
                PropertiesJson = propertiesJson,
            };

            await dbContext.CannedMessages.AddAsync(newMessage, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _publisher.Publish(new CannedMessageChangedNotification(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish {Notification} after saving canned message.", nameof(CannedMessageChangedNotification));
        }

        return Unit.Value;
    }
}
