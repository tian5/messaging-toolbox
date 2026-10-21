using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Core.Interfaces;

public interface IAuditLog
{
    Task WriteAsync(string action, 
        string connectionName, 
        string entityPath,
        MessageSubQueue subQueue, 
        string? details, 
        CancellationToken cancellationToken);
}
