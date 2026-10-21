namespace MessagingToolbox.Abstractions.Errors;

/// <summary>The addressed queue/topic/subscription does not exist on the broker.</summary>
public sealed class MessagingEntityNotFoundException : MessagingProviderException
{
    public MessagingEntityNotFoundException(string entityPath)
        : base($"Entity '{entityPath}' was not found.")
    {
        EntityPath = entityPath;
    }

    public MessagingEntityNotFoundException(string entityPath, Exception innerException)
        : base($"Entity '{entityPath}' was not found.", innerException)
    {
        EntityPath = entityPath;
    }

    public string EntityPath { get; }
}
