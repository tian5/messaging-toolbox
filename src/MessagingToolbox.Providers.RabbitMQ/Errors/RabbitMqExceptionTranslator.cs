using System.Net;
using MessagingToolbox.Abstractions.Errors;
using RabbitMQ.Client.Exceptions;

namespace MessagingToolbox.Providers.RabbitMQ.Errors;

/// <summary>
/// Translates RabbitMQ client and management API exceptions into the typed provider errors
/// (architecture doc §9) at the provider boundary — raw SDK exceptions never reach the UI.
/// </summary>
public static class RabbitMqExceptionTranslator
{
    private const string UnavailableHint = "If the broker runs in Aspire or Docker, it may still be starting.";

    // AMQP 0-9-1 reply codes (channel/connection close).
    private const ushort AccessRefused = 403;
    private const ushort NotFound = 404;
    private const ushort PreconditionFailed = 406;

    /// <summary>True for the exception types <see cref="Translate"/> maps; use as a catch filter.</summary>
    public static bool CanTranslate(Exception exception)
        => exception is RabbitMQClientException or OperationInterruptedException or BrokerUnreachableException
            or HttpRequestException;

    public static Exception Translate(Exception exception, string entityPath) => exception switch
    {
        BrokerUnreachableException { InnerException: AuthenticationFailureException } => new MessagingProviderException(
            "The broker rejected the credentials in the connection string.", exception),
        BrokerUnreachableException => new MessagingProviderUnavailableException(
            $"The RabbitMQ broker could not be reached. {UnavailableHint}", exception),
        PublishException { IsReturn: true } => new MessagingProviderException(
            $"The message to '{entityPath}' was not routed — no queue is bound for its routing key.", exception),
        AlreadyClosedException => new MessagingProviderUnavailableException(
            $"The connection to the RabbitMQ broker was closed. {UnavailableHint}", exception),
        OperationInterruptedException { ShutdownReason.ReplyCode: NotFound } => new MessagingEntityNotFoundException(entityPath, exception),
        OperationInterruptedException { ShutdownReason.ReplyCode: AccessRefused } => new MessagingProviderException(
            $"Access to '{entityPath}' was refused — the user lacks the permission (configure, write or read) this operation needs.", exception),
        OperationInterruptedException { ShutdownReason.ReplyCode: PreconditionFailed } interrupted => new MessagingProviderException(
            $"The broker rejected the request on '{entityPath}': {interrupted.ShutdownReason?.ReplyText}", exception),
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => new MessagingProviderException(
            "The RabbitMQ management API rejected the credentials — the user needs the 'monitoring' or 'management' tag.", exception),
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => new MessagingEntityNotFoundException(entityPath, exception),
        HttpRequestException => new MessagingProviderUnavailableException(
            $"The RabbitMQ management API could not be reached. {UnavailableHint}", exception),
        _ => new MessagingProviderException($"The broker rejected the request on '{entityPath}': {exception.Message}", exception),
    };
}
