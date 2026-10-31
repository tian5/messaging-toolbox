using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.MoveMessages;

public sealed class MoveMessagesCommandHandler : IRequestHandler<MoveMessagesCommand, long>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public MoveMessagesCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<long> Handle(MoveMessagesCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);
        if (!provider.Capabilities.CanMoveMessages)
        {
            throw new NotSupportedByProviderException("This connection can't move messages inside the broker.");
        }

        long moved = await provider.MoveMessagesAsync(request.SourcePath, request.DestinationPath, cancellationToken);

        if (moved > 0)
        {
            ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(request.ConnectionId, cancellationToken);
            await _auditLog.WriteAsync(
                AuditLog.Move,
                connection?.Name ?? request.ConnectionId.ToString(),
                request.SourcePath,
                MessageSubQueue.Active,
                $"{moved} message(s) moved to {request.DestinationPath}",
                cancellationToken);
        }

        return moved;
    }
}
