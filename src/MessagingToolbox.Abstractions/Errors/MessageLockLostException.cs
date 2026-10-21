namespace MessagingToolbox.Abstractions.Errors;

/// <summary>A settlement failed because the message lock expired or was lost.</summary>
public sealed class MessageLockLostException : MessagingProviderException
{
    public MessageLockLostException(string message)
        : base(message)
    {
    }

    public MessageLockLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
