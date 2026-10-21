using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Connections.Notifications;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Connections.Commands.DeleteConnection;

public sealed class DeleteConnectionCommandHandler : IRequestHandler<DeleteConnectionCommand>
{
    private readonly IToolboxDbContextFactory _dbContextFactory;
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IAuditLog _auditLog;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteConnectionCommandHandler> _logger;

    public DeleteConnectionCommandHandler(
        IToolboxDbContextFactory dbContextFactory,
        IMessagingProviderFactory providerFactory,
        IAuditLog auditLog,
        IPublisher publisher,
        ILogger<DeleteConnectionCommandHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _providerFactory = providerFactory;
        _auditLog = auditLog;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(DeleteConnectionCommand request, CancellationToken cancellationToken)
    {
        await using IToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        ConnectionProfile? profile = await dbContext.ConnectionProfiles
            .FindAsync([request.ConnectionId], cancellationToken);

        if (profile is not null)
        {
            dbContext.ConnectionProfiles.Remove(profile);
            await dbContext.SaveChangesAsync(cancellationToken);
            await _auditLog.WriteAsync(
                AuditLog.DeleteConnection,
                profile.DisplayName,
                string.Empty,
                MessageSubQueue.Active,
                "Saved connection deleted",
                cancellationToken);
        }

        await _providerFactory.DisposeAsync(request.ConnectionId);

        try
        {
            await _publisher.Publish(new SavedConnectionChangedNotification(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish {Notification} after deleting connection.", nameof(SavedConnectionChangedNotification));
        }

        return Unit.Value;
    }
}
