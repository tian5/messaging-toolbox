namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// Per-circuit confirmation gate for destructive operations (NFR-3): the pending request holds the
/// deferred action until the user confirms or cancels.
/// </summary>
public sealed class ConfirmationService
{
    private Func<Task>? _pendingAction;

    public string? Title { get; private set; }

    public string? Message { get; private set; }

    public bool HasPendingRequest => _pendingAction is not null;

    /// <summary>
    /// Raised when a request is opened or closed. The dialog is rendered by the page, so a request
    /// raised from a child component (e.g. the composer) would otherwise not show until the page
    /// happens to re-render.
    /// </summary>
    public event Action? Changed;

    public void Request(string title, string message, Func<Task> action)
    {
        Title = title;
        Message = message;
        _pendingAction = action;
        Changed?.Invoke();
    }

    public async Task ConfirmAsync()
    {
        Func<Task>? action = _pendingAction;
        _pendingAction = null;
        Changed?.Invoke();

        if (action is not null)
        {
            await action();
        }
    }

    public void Cancel()
    {
        _pendingAction = null;
        Changed?.Invoke();
    }
}
