using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Connections.Queries.TestConnection;

public sealed record TestConnectionQuery(Guid ConnectionId) : IRequest<TestConnectionResultDto>;
