namespace MessagingToolbox.Abstractions.Errors;

/// <summary>
/// The active connection does not support the requested operation — check
/// <see cref="Messaging.ProviderCapabilities"/> before acting. The UI renders an explanatory
/// hint for this instead of an error bar.
/// </summary>
public sealed class NotSupportedByProviderException : MessagingProviderException
{
    public NotSupportedByProviderException(string message)
        : base(message)
    {
    }

    public NotSupportedByProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
