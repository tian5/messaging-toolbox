namespace MessagingToolbox.Abstractions.Errors;

/// <summary>
/// Base type for typed provider errors. Provider SDK exceptions are
/// translated at the provider boundary into these — they never reach the UI raw.
/// </summary>
public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message)
        : base(message)
    {
    }

    public MessagingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
