using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;

public sealed record PeekMessagesQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    long? FromSequenceNumber,
    int MaxMessages,
    string? SessionId = null,
    DateTimeOffset? FromTime = null) : IRequest<IReadOnlyList<MessageDto>>;
