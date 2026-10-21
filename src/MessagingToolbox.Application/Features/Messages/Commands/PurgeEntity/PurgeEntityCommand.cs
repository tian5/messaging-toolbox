using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.PurgeEntity;

/// <summary>Purges a sub-queue via batched receive-and-delete; returns the number deleted. </summary>
public sealed record PurgeEntityCommand(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue) : IRequest<long>;
