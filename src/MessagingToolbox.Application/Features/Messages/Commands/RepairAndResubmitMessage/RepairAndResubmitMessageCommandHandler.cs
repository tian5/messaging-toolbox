using System.Text;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.RepairAndResubmitMessage;

public sealed class RepairAndResubmitMessageCommandHandler : IRequestHandler<RepairAndResubmitMessageCommand>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessageTemplateEngine _templateEngine;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public RepairAndResubmitMessageCommandHandler(
        IMessagingProviderFactory providerFactory,
        IMessageTemplateEngine templateEngine,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _templateEngine = templateEngine;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<Unit> Handle(RepairAndResubmitMessageCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        ToolboxMessage original = request.OriginalMessage is not null
            ? MessageDtoMapper.ToToolboxMessage(request.OriginalMessage)
            : new ToolboxMessage { Body = Encoding.UTF8.GetBytes(_templateEngine.Render(request.Body)) };

        ResubmitOverrides overrides = new(
            Body: _templateEngine.Render(request.Body),
            ContentType: request.ContentType,
            Subject: request.Subject,
            CorrelationId: request.CorrelationId,
            SessionId: request.SessionId,
            ApplicationProperties: ApplicationPropertyValues.ToTyped(request.ApplicationProperties, request.PropertyTypes, _templateEngine.Render));

        ToolboxMessage repaired = await provider.PrepareResubmitAsync(
            request.OriginalEntityPath ?? request.TargetPath,
            request.OriginalSubQueue,
            original,
            overrides,
            cancellationToken);

        await provider.SendAsync(request.TargetPath, [repaired], cancellationToken);

        // Clone semantics made explicit (FR-4.6): the original is settled only on request,
        // only after the copy landed.
        if (request is { RemoveOriginalAfterSend: true, OriginalEntityPath: not null })
        {
            await RemoveOriginalAsync(provider, request, cancellationToken);
        }

        return Unit.Value;
    }

    /// <summary>
    /// The original is completed via its held lock token — settlement touches exactly that
    /// message and nothing else. A peeked original carries no lock token and is left untouched.
    /// </summary>
    private async Task RemoveOriginalAsync(
        IMessagingProvider provider,
        RepairAndResubmitMessageCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OriginalLockToken is null)
        {
            return;
        }

        IReadOnlyList<string> removed = await provider.SettleAsync(
            new SettleRequest(
                request.OriginalEntityPath!,
                request.OriginalSubQueue,
                SettlementOperation.Complete,
                [request.OriginalLockToken],
                null,
                null),
            cancellationToken);

        if (removed.Count == 0)
        {
            return;
        }

        string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.RepairResubmitRemove,
            connectionName,
            request.OriginalEntityPath!,
            request.OriginalSubQueue,
            $"Copy sent to '{request.TargetPath}', original removed (sequence: {request.OriginalSequenceNumber})",
            cancellationToken);
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }
}
