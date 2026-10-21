using Mediator;

namespace MessagingToolbox.Application.Features.Connections.Commands.DeleteConnection;

/// <summary>Removes a saved connection; config-seeded connections cannot be deleted.</summary>
public sealed record DeleteConnectionCommand(Guid ConnectionId) : IRequest;
