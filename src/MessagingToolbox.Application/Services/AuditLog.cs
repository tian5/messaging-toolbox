using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Audit.Notifications;
using MessagingToolbox.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Services;

/// <summary>
/// Central write of destructive-operation audit records. The audit runs after the broker
/// operation already succeeded, so a failed write is logged — never surfaced as a failure of
/// the operation itself, which would misreport what happened to the user's messages.
/// </summary>
public sealed class AuditLog(IToolboxDbContextFactory dbContextFactory, IPublisher publisher, ILogger<AuditLog> logger) : IAuditLog
{
    public const string ReceiveDelete = "receive-delete";
    public const string SettleComplete = "settle-complete";
    public const string SettleDeadLetter = "settle-deadletter";
    public const string Purge = "purge";
    public const string DeleteSelected = "delete-selected";
    public const string DeadLetterSelected = "deadletter-selected";
    public const string RepairResubmitRemove = "repair-resubmit-remove";
    public const string Requeue = "requeue";
    public const string Move = "move";
    public const string Resend = "resend";
    public const string DeleteEntity = "delete-entity";
    public const string SaveEntity = "save-entity";
    public const string CreateRoutingRule = "create-routing-rule";
    public const string DeleteRoutingRule = "delete-routing-rule";
    public const string Import = "import";
    public const string DeleteConnection = "delete-connection";

    public async Task WriteAsync(
        string action,
        string connectionName,
        string entityPath,
        MessageSubQueue subQueue,
        string? details,
        CancellationToken cancellationToken)
    {
        AuditEntry newAuditEntry = new()
        {
            Action = action,
            ConnectionName = connectionName,
            EntityPath = entityPath,
            SubQueue = subQueue,
            Details = details,
        };

        try
        {
            await using IToolboxDbContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await dbContext.AuditEntries.AddAsync(newAuditEntry, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Writing the {Action} audit entry for {EntityPath} failed; the operation itself completed.", action, entityPath);
            return;
        }

        try
        {
            await publisher.Publish(new AuditEntryWrittenNotification(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to publish {Notification} after writing an audit entry.", nameof(AuditEntryWrittenNotification));
        }
    }
}
