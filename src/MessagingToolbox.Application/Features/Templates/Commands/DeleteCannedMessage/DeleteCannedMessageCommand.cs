using Mediator;

namespace MessagingToolbox.Application.Features.Templates.Commands.DeleteCannedMessage;

public sealed record DeleteCannedMessageCommand(Guid Id) : IRequest;
