using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Domain.Entities;

/// <summary>
/// Record of a destructive operation (NFR-3): who/what/when, entity, and affected messages
/// (e.g. sequence numbers). Written by handlers, shown in the in-app audit panel.
/// </summary>
public class AuditEntry : EntityBase
{
    /// <summary>Machine name of the operation, e.g. "purge", "receive-delete", "settle-complete".</summary>
    public string Action { get; set; } = string.Empty;

    public string ConnectionName { get; set; } = string.Empty;

    public string EntityPath { get; set; } = string.Empty;

    public MessageSubQueue SubQueue { get; set; } = MessageSubQueue.Active;

    /// <summary>Free-form detail, e.g. "12 messages deleted" or the affected sequence numbers.</summary>
    public string? Details { get; set; }
}
