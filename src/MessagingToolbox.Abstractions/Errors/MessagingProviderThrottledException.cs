namespace MessagingToolbox.Abstractions.Errors;

/// <summary>
/// The broker throttled the request (or timed out under load) — the namespace is busy;
/// wait a few seconds before retrying. Surfaced so the UI can show an actionable hint
/// instead of the raw SDK exception.
/// </summary>
public sealed class MessagingProviderThrottledException : MessagingProviderException
{
    public MessagingProviderThrottledException(string message)
        : base(message)
    {
    }

    public MessagingProviderThrottledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
