using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Errors;

namespace MessagingToolbox.Providers.AzureServiceBus.Errors;

/// <summary>
/// Translates Azure Service Bus SDK exceptions into the typed provider errors (architecture doc §9)
/// at the provider boundary — raw SDK exceptions never reach the UI. Besides
/// <see cref="ServiceBusException"/>, the SDK reports rejected credentials as
/// <see cref="UnauthorizedAccessException"/>, and the administration client (HTTP, port 5300 on the
/// emulator) reports an unreachable endpoint as <see cref="HttpRequestException"/>.
/// </summary>
public static class ServiceBusExceptionTranslator
{
    private const string UnavailableHint =
        "If this is the Azure Service Bus emulator, it may still be starting (it waits for its SQL Server companion).";

    /// <summary>True for the SDK exception types <see cref="Translate"/> maps; use as a catch filter.</summary>
    public static bool CanTranslate(Exception exception)
        => exception is ServiceBusException or UnauthorizedAccessException or HttpRequestException
            || IsSessionRequirement(exception)
            || IsSessionlessEntity(exception);

    /// <summary>
    /// True when the broker rejected a non-session receive or a session-less send because the entity
    /// requires sessions. The SDK reports both as a plain <see cref="InvalidOperationException"/>
    /// (AMQP "not-allowed"), so the message text is the only discriminator — cloud and emulator
    /// word it the same way: "…an entity that requires sessions…" and "The SessionId was not set…".
    /// </summary>
    public static bool IsSessionRequirement(Exception exception)
        => exception is InvalidOperationException
            && (exception.Message.Contains("requires sessions", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("SessionId was not set", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when a session receiver was requested on an entity that doesn't use sessions — also an
    /// <see cref="InvalidOperationException"/> ("…an entity that does not require sessions…").
    /// </summary>
    public static bool IsSessionlessEntity(Exception exception)
        => exception is InvalidOperationException
            && exception.Message.Contains("does not require sessions", StringComparison.OrdinalIgnoreCase);

    /// <summary>The typed error for a session-less send to an entity that requires sessions.</summary>
    public static MessagingProviderException SessionIdRequired(string entityPath, Exception? innerException = null)
    {
        string message = $"'{entityPath}' requires sessions — set a Session ID on every message sent to it.";
        return innerException is null ? new MessagingProviderException(message) : new MessagingProviderException(message, innerException);
    }

    public static Exception Translate(Exception exception, string entityPath) => exception switch
    {
        ServiceBusException serviceBusException => TranslateServiceBus(serviceBusException, entityPath),
        UnauthorizedAccessException => new MessagingProviderException(
            $"Access to '{entityPath}' was denied — the connection string lacks the claim (Send, Listen or Manage) this operation needs.", exception),
        HttpRequestException => new MessagingProviderUnavailableException(
            $"The broker's management endpoint could not be reached. {UnavailableHint}", exception),
        InvalidOperationException when IsSessionlessEntity(exception) => new MessagingProviderException(
            $"'{entityPath}' doesn't use sessions — clear the session filter.", exception),
        InvalidOperationException when IsSessionRequirement(exception) => new MessagingProviderException(
            $"'{entityPath}' requires sessions — messages need a Session ID and receives go through sessions.", exception),
        _ => exception,
    };

    private static Exception TranslateServiceBus(ServiceBusException exception, string entityPath) => exception.Reason switch
    {
        ServiceBusFailureReason.MessageLockLost => new MessageLockLostException(
            "The message lock was lost — the message may have been settled already or its lock expired.", exception),
        ServiceBusFailureReason.SessionLockLost => new MessageLockLostException(
            "The session lock was lost — receive the message again to continue.", exception),
        ServiceBusFailureReason.MessagingEntityNotFound => new MessagingEntityNotFoundException(entityPath, exception),
        ServiceBusFailureReason.MessagingEntityAlreadyExists => new MessagingProviderException(
            $"'{entityPath}' already exists.", exception),
        ServiceBusFailureReason.MessagingEntityDisabled => new MessagingProviderException(
            $"'{entityPath}' is disabled — enable it before sending or receiving.", exception),
        ServiceBusFailureReason.MessageSizeExceeded => new MessagingProviderException(
            "The message exceeds the broker's size limit for this connection.", exception),
        ServiceBusFailureReason.QuotaExceeded => new MessagingProviderException(
            $"A quota on '{entityPath}' was exceeded (for example the entity's maximum size or the namespace's connection limit).", exception),
        ServiceBusFailureReason.SessionCannotBeLocked => new MessagingProviderException(
            $"The session on '{entityPath}' is locked by another receiver — wait for it to be released or pick another session.", exception),
        ServiceBusFailureReason.ServiceBusy => new MessagingProviderThrottledException(
            "The namespace is throttled — wait a few seconds before retrying.", exception),
        ServiceBusFailureReason.ServiceTimeout => new MessagingProviderUnavailableException(
            $"The broker did not respond in time — retry in a few seconds. {UnavailableHint}", exception),
        ServiceBusFailureReason.ServiceCommunicationProblem => new MessagingProviderUnavailableException(
            $"The broker could not be reached. {UnavailableHint}", exception),
        _ => new MessagingProviderException($"The broker rejected the request on '{entityPath}': {exception.Message}", exception),
    };
}
