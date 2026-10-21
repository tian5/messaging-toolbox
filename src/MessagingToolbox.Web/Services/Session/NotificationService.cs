namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// Per-circuit notification stack (scoped): several concurrent operations (e.g. fetches on
/// different entities) can report at the same time; the connection badge shows where each belongs.
/// </summary>
public sealed class NotificationService
{
    private readonly List<Notification> _notifications = [];

    public IReadOnlyList<Notification> Notifications => _notifications;

    /// <summary>Raised after any change so the notification UI can re-render itself.</summary>
    public event Action? OnChanged;

    public Notification Notify(string text, bool pending = false, string? connectionName = null)
    {
        Notification notification = new()
        {
            Text = text,
            Pending = pending,
            ConnectionName = connectionName,
        };
        _notifications.Add(notification);
        OnChanged?.Invoke();
        return notification;
    }

    /// <summary>
    /// Resolves a pending notification with its final text. Mutating the instance directly would
    /// not re-render the stack, which only listens to <see cref="OnChanged"/>.
    /// </summary>
    public void Complete(Notification notification, string text)
    {
        notification.Pending = false;
        notification.Text = text;
        OnChanged?.Invoke();
    }

    public void Dismiss(Notification notification)
    {
        if (_notifications.Remove(notification))
        {
            OnChanged?.Invoke();
        }
    }

    public void Clear()
    {
        if (_notifications.Count == 0)
        {
            return;
        }

        _notifications.Clear();
        OnChanged?.Invoke();
    }
}

public sealed class Notification
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string Text { get; set; }

    public bool Pending { get; set; }

    /// <summary>Display name of the connection the event belongs to, so stacked notifications stay attributable.</summary>
    public string? ConnectionName { get; set; }
}
