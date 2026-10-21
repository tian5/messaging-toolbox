using Mediator;
using MessagingToolbox.Application.Common;

namespace MessagingToolbox.Application.Features.Messages.Commands.SendMessage;

/// <summary>Sends one or more copies of a composed message; body and application properties go
/// through placeholder templating per copy. <paramref name="PropertyTypes"/> types the property
/// values (text when absent).</summary>
public sealed record SendMessageCommand(
    Guid ConnectionId,
    string EntityPath,
    string Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> ApplicationProperties,
    string? Subject,
    string? MessageId,
    string? CorrelationId,
    string? SessionId,
    string? To,
    string? ReplyTo,
    DateTimeOffset? ScheduledEnqueueTime,
    TimeSpan? TimeToLive,
    int Copies,
    IReadOnlyDictionary<string, PropertyValueType>? PropertyTypes = null) : IRequest;
