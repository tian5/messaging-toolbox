using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Explorer.Queries.GetEntityTree;

public sealed record GetEntityTreeQuery(Guid ConnectionId) : IRequest<IReadOnlyList<EntityTreeNodeDto>>;
