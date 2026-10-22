using System.Text;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Common;

public static class MessageDtoMapper
{
    public static MessageDto ToDto(ToolboxMessage message)
    {
        string body = Encoding.UTF8.GetString(message.Body);

        return new MessageDto
        {
            Body = body,
            BodyFormat = MessageBodyFormatDetector.Detect(body),
            BodySizeBytes = message.Body.Length,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            SessionId = message.SessionId,
            ReplyToSessionId = message.ReplyToSessionId,
            Subject = message.Subject,
            To = message.To,
            ReplyTo = message.ReplyTo,
            PartitionKey = message.PartitionKey,
            ContentType = message.ContentType,
            TimeToLive = message.TimeToLive,
            ScheduledEnqueueTime = message.ScheduledEnqueueTime,
            RoutingKey = message.RoutingKey,
            SequenceNumber = message.SequenceNumber,
            State = message.State,
            DeliveryCount = message.DeliveryCount,
            EnqueuedTime = message.EnqueuedTime,
            ExpiresAt = message.ExpiresAt,
            DeadLetterReason = message.DeadLetterReason,
            DeadLetterErrorDescription = message.DeadLetterErrorDescription,
            DeadLetterSourcePath = message.DeadLetterSourcePath,
            LockToken = message.LockToken,
            LockedUntil = message.LockedUntil,
            ApplicationProperties = message.ApplicationProperties,
            BrokerProperties = message.BrokerProperties,
        };
    }

    /// <summary>Rebuilds a <see cref="ToolboxMessage"/> from a DTO for resend/repair flows.</summary>
    public static ToolboxMessage ToToolboxMessage(MessageDto message) => new()
    {
        Body = Encoding.UTF8.GetBytes(message.Body),
        ContentType = message.ContentType,
        Subject = message.Subject,
        MessageId = message.MessageId,
        CorrelationId = message.CorrelationId,
        SessionId = message.SessionId,
        ReplyToSessionId = message.ReplyToSessionId,
        To = message.To,
        ReplyTo = message.ReplyTo,
        PartitionKey = message.PartitionKey,
        ScheduledEnqueueTime = message.ScheduledEnqueueTime,
        TimeToLive = message.TimeToLive,
        RoutingKey = message.RoutingKey,
        ApplicationProperties = message.ApplicationProperties,
    };

    /// <summary>Builds a send-ready message from editable fields; settlement metadata is never copied.</summary>
    public static ToolboxMessage ToToolboxMessage(
        string body,
        string? contentType,
        IReadOnlyDictionary<string, object?> applicationProperties,
        string? subject = null,
        string? messageId = null,
        string? correlationId = null,
        string? sessionId = null,
        string? to = null,
        string? replyTo = null,
        DateTimeOffset? scheduledEnqueueTime = null,
        TimeSpan? timeToLive = null,
        string? routingKey = null) => new()
    {
        Body = Encoding.UTF8.GetBytes(body),
        ContentType = contentType,
        Subject = subject,
        MessageId = messageId,
        CorrelationId = correlationId,
        SessionId = sessionId,
        To = to,
        ReplyTo = replyTo,
        ScheduledEnqueueTime = scheduledEnqueueTime,
        TimeToLive = timeToLive,
        RoutingKey = routingKey,
        ApplicationProperties = applicationProperties,
    };
}
