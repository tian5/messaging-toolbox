namespace MessagingToolbox.Domain.Entities;

/// <summary>Base for persisted tool state; timestamps are stamped centrally by the DbContext.</summary>
public abstract class EntityBase
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }
}
