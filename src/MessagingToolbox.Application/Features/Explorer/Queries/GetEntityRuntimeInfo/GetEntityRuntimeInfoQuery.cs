using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Explorer.Queries.GetEntityRuntimeInfo;

public sealed record GetEntityRuntimeInfoQuery(Guid ConnectionId, string EntityPath) : IRequest<EntityRuntimeInfoDto?>;
