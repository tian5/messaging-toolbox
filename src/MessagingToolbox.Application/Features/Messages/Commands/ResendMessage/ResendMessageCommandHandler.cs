using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.ResendMessage;

public sealed class ResendMessageCommandHandler : IRequestHandler<ResendMessageCommand>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public ResendMessageCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<Unit> Handle(ResendMessageCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        ToolboxMessage clone = await provider.PrepareResubmitAsync(
            request.OriginalEntityPath,
            request.OriginalSubQueue,
            MessageDtoMapper.ToToolboxMessage(request.Message),
            ResubmitOverrides.Empty,
            cancellationToken);

        await provider.SendAsync(request.TargetPath, [clone], cancellationToken);

        string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.Resend,
            connectionName,
            request.OriginalEntityPath,
            request.OriginalSubQueue,
            $"Copy sent to '{request.TargetPath}' (sequence: {request.Message.SequenceNumber})",
            cancellationToken);

        return Unit.Value;
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
