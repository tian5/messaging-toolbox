using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Templates.Queries.GetCannedMessages;

public sealed record GetCannedMessagesQuery : IRequest<IReadOnlyList<CannedMessageDto>>;
