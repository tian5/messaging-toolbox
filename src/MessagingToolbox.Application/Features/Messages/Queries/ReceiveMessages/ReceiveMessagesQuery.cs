using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveMessages;

public sealed record ReceiveMessagesQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    int MaxMessages,
    TimeSpan? MaxWaitTime,
    string? SessionId = null) : IRequest<IReadOnlyList<MessageDto>>;
