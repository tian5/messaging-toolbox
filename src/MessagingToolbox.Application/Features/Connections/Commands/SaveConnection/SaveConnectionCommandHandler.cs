using Mediator;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Connections.Notifications;
using MessagingToolbox.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Connections.Commands.SaveConnection;

public sealed class SaveConnectionCommandHandler : IRequestHandler<SaveConnectionCommand>
{
    private readonly IToolboxDbContextFactory _dbContextFactory;
    private readonly IConnectionStringProtector _protector;
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IPublisher _publisher;
    private readonly ILogger<SaveConnectionCommandHandler> _logger;

    public SaveConnectionCommandHandler(
        IToolboxDbContextFactory dbContextFactory,
        IConnectionStringProtector protector,
        IMessagingProviderFactory providerFactory,
        IPublisher publisher,
        ILogger<SaveConnectionCommandHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _protector = protector;
        _providerFactory = providerFactory;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(SaveConnectionCommand request, CancellationToken cancellationToken)
    {
        await using IToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        ConnectionProfile profile;
        if (request.Id is { } id)
        {
            profile = (await dbContext.ConnectionProfiles.FindAsync([request.Id], cancellationToken))
                ?? throw new ArgumentException($"Connection '{id}' was not found.", nameof(request));
            profile.DisplayName = request.DisplayName;
            if (!string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                profile.ProtectedConnectionString = _protector.Protect(request.ConnectionString);
            }
        }
        else
        {
            profile = new ConnectionProfile
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName,
                Kind = request.Kind,
                ProtectedConnectionString = _protector.Protect(request.ConnectionString),
            };

            await dbContext.ConnectionProfiles.AddAsync(profile, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await _providerFactory.DisposeAsync(profile.Id);

        try
        {
            await _publisher.Publish(new SavedConnectionChangedNotification(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish {Notification} after saving connection.", nameof(SavedConnectionChangedNotification));
        }

        return Unit.Value;
    }
}
