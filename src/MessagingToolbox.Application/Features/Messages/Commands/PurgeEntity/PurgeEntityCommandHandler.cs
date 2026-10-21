using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.PurgeEntity;

public sealed class PurgeEntityCommandHandler : IRequestHandler<PurgeEntityCommand, long>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public PurgeEntityCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<long> Handle(PurgeEntityCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        long deleted = await provider.PurgeAsync(request.EntityPath, request.SubQueue, cancellationToken);

        if (deleted > 0)
        {
            string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
            await _auditLog.WriteAsync(
                AuditLog.Purge,
                connectionName,
                request.EntityPath,
                request.SubQueue,
                $"{deleted} message(s) deleted",
                cancellationToken);
        }

        return deleted;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
