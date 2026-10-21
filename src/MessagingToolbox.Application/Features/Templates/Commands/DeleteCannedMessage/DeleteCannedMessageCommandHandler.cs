using Mediator;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Templates.Notifications;
using MessagingToolbox.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Templates.Commands.DeleteCannedMessage;

public sealed class DeleteCannedMessageCommandHandler : IRequestHandler<DeleteCannedMessageCommand>
{
    private readonly IToolboxDbContextFactory _dbContextFactory;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteCannedMessageCommandHandler> _logger;

    public DeleteCannedMessageCommandHandler(
        IToolboxDbContextFactory dbContextFactory,
        IPublisher publisher,
        ILogger<DeleteCannedMessageCommandHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(DeleteCannedMessageCommand request, CancellationToken cancellationToken)
    {
        await using IToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        CannedMessage? message = await dbContext.CannedMessages
            .FindAsync([request.Id], cancellationToken);

        if (message is not null)
        {
            dbContext.CannedMessages.Remove(message);
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                await _publisher.Publish(new CannedMessageChangedNotification(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to publish {Notification} after deleting canned message.", nameof(CannedMessageChangedNotification));
            }
        }

        return Unit.Value;
    }
}
