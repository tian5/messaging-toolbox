using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>Maps between the Azure Service Bus SDK message types and the normalized ToolboxMessage.</summary>
public static class AzureServiceBusMessageMapper
{
    public static ToolboxMessage ToToolboxMessage(ServiceBusReceivedMessage message) => new()
    {
        MessageId = message.MessageId,
        CorrelationId = message.CorrelationId,
        SessionId = message.SessionId,
        ReplyToSessionId = message.ReplyToSessionId,
        Subject = message.Subject,
        To = message.To,
        ReplyTo = message.ReplyTo,
        PartitionKey = message.PartitionKey,
        ContentType = message.ContentType,
        TimeToLive = message.TimeToLive == TimeSpan.MaxValue ? null : message.TimeToLive,
        ScheduledEnqueueTime = message.ScheduledEnqueueTime == default ? null : message.ScheduledEnqueueTime,
        Body = message.Body.ToArray(),
        ApplicationProperties = new Dictionary<string, object?>(message.ApplicationProperties),
        SequenceNumber = message.SequenceNumber,
        State = message.State switch
        {
            ServiceBusMessageState.Deferred => MessageState.Deferred,
            ServiceBusMessageState.Scheduled => MessageState.Scheduled,
            _ => MessageState.Active,
        },
        DeliveryCount = message.DeliveryCount,
        EnqueuedTime = message.EnqueuedTime == default ? null : message.EnqueuedTime,
        ExpiresAt = message.ExpiresAt == default ? null : message.ExpiresAt,
        // Peeked messages report Guid.Empty as their lock token; only real (peek-lock) tokens
        // may flow to the UI, otherwise peeked messages look settleable.
        LockToken = string.IsNullOrEmpty(message.LockToken) || message.LockToken == Guid.Empty.ToString()
            ? null
            : message.LockToken,
        DeadLetterReason = message.DeadLetterReason,
        DeadLetterErrorDescription = message.DeadLetterErrorDescription,
        LockedUntil = message.LockedUntil == default ? null : message.LockedUntil,
    };

    public static ServiceBusMessage ToServiceBusMessage(ToolboxMessage message)
    {
        // SDK property setters reject null — assign only what is present.
        ServiceBusMessage serviceBusMessage = new(new BinaryData(message.Body));

        if (message.MessageId is not null)
        {
            serviceBusMessage.MessageId = message.MessageId;
        }

        if (message.CorrelationId is not null)
        {
            serviceBusMessage.CorrelationId = message.CorrelationId;
        }

        if (message.SessionId is not null)
        {
            serviceBusMessage.SessionId = message.SessionId;
        }

        if (message.ReplyToSessionId is not null)
        {
            serviceBusMessage.ReplyToSessionId = message.ReplyToSessionId;
        }

        if (message.Subject is not null)
        {
            serviceBusMessage.Subject = message.Subject;
        }

        if (message.To is not null)
        {
            serviceBusMessage.To = message.To;
        }

        if (message.ReplyTo is not null)
        {
            serviceBusMessage.ReplyTo = message.ReplyTo;
        }

        if (message.PartitionKey is not null)
        {
            serviceBusMessage.PartitionKey = message.PartitionKey;
        }

        if (message.ContentType is not null)
        {
            serviceBusMessage.ContentType = message.ContentType;
        }

        if (message.TimeToLive.HasValue)
        {
            serviceBusMessage.TimeToLive = message.TimeToLive.Value;
        }

        if (message.ScheduledEnqueueTime.HasValue)
        {
            serviceBusMessage.ScheduledEnqueueTime = message.ScheduledEnqueueTime.Value;
        }

        foreach (KeyValuePair<string, object?> property in message.ApplicationProperties)
        {
            if (property.Value is not null)
            {
                serviceBusMessage.ApplicationProperties[property.Key] = property.Value;
            }
        }

        return serviceBusMessage;
    }
}
