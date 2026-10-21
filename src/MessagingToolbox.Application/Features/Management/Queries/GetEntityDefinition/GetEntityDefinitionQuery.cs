using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Management.Queries.GetEntityDefinition;

public sealed record GetEntityDefinitionQuery(Guid ConnectionId, string EntityPath) : IRequest<EntityDefinitionDto>;
