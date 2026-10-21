using Mediator;

namespace MessagingToolbox.Application.Features.Management.Commands.DeleteEntity;

public sealed record DeleteEntityCommand(Guid ConnectionId, string EntityPath) : IRequest;
