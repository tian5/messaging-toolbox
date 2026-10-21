namespace MessagingToolbox.Abstractions.Errors;

/// <summary>
/// The broker could not be reached or did not answer in time — typically a stopped namespace, a
/// network problem, or an emulator that is still starting. Distinct from
/// <see cref="MessagingProviderThrottledException"/> (reachable but busy) so the UI can say which.
/// </summary>
public sealed class MessagingProviderUnavailableException : MessagingProviderException
{
    public MessagingProviderUnavailableException(string message)
        : base(message)
    {
    }

    public MessagingProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
