using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Connections.Queries.GetConnections;

public sealed record GetConnectionsQuery : IRequest<IReadOnlyList<ConnectionDto>>;
