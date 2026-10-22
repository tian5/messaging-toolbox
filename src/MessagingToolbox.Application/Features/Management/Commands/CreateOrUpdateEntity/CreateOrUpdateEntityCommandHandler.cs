using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Management.Notifications;
using MessagingToolbox.Application.Services;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateOrUpdateEntity;

public sealed class CreateOrUpdateEntityCommandHandler : IRequestHandler<CreateOrUpdateEntityCommand, string>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateOrUpdateEntityCommandHandler> _logger;

    public CreateOrUpdateEntityCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog,
        IPublisher publisher,
        ILogger<CreateOrUpdateEntityCommandHandler> logger)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<string> Handle(CreateOrUpdateEntityCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        EntityDefinition definition = new(request.Name, request.Kind, request.ParentPath, request.Options);
        MessagingEntity entity = await provider.CreateOrUpdateEntityAsync(definition, cancellationToken);

        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.SaveEntity,
            connection?.Name ?? request.ConnectionId.ToString(),
            entity.Path,
            MessageSubQueue.Active,
            $"{request.Kind} created or updated",
            cancellationToken);

        try
        {
            await _publisher.Publish(new EntityListChangedNotification(request.ConnectionId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Publishing entity list change for connection {ConnectionId} failed", request.ConnectionId);
        }

        return entity.Path;
    }
}
