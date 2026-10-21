using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Management.Notifications;
using MessagingToolbox.Application.Services;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Management.Commands.DeleteEntity;

public sealed class DeleteEntityCommandHandler : IRequestHandler<DeleteEntityCommand>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteEntityCommandHandler> _logger;

    public DeleteEntityCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog,
        IPublisher publisher,
        ILogger<DeleteEntityCommandHandler> logger)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(DeleteEntityCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        await provider.DeleteEntityAsync(request.EntityPath, cancellationToken);

        string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.DeleteEntity,
            connectionName,
            request.EntityPath,
            MessageSubQueue.Active,
            "Entity deleted",
            cancellationToken);

        try
        {
            await _publisher.Publish(new EntityListChangedNotification(request.ConnectionId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Publishing entity list change for connection {ConnectionId} failed", request.ConnectionId);
        }

        return Unit.Value;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
