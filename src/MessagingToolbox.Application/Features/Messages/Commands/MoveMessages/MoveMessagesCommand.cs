using Mediator;

namespace MessagingToolbox.Application.Features.Messages.Commands.MoveMessages;

/// <summary>Moves every ready message of the source to the destination inside the broker; returns the number moved.</summary>
public sealed record MoveMessagesCommand(
    Guid ConnectionId,
    string SourcePath,
    string DestinationPath) : IRequest<long>;
