using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Queries.ExportMessages;

/// <summary>Exports messages of the current view to a JSON file (FR-5.4); optional sequence-number filter for selections.</summary>
public sealed record ExportMessagesQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    int MaxMessages,
    long? FromSequenceNumber,
    IReadOnlyList<long>? SequenceNumbers,
    IReadOnlyList<MessageDto>? LoadedMessages = null,
    string? SessionId = null) : IRequest<ExportedMessagesFileDto>;
