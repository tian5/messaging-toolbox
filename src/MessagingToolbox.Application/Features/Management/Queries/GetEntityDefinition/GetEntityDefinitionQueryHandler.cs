using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Management.Queries.GetEntityDefinition;

public sealed class GetEntityDefinitionQueryHandler : IRequestHandler<GetEntityDefinitionQuery, EntityDefinitionDto>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetEntityDefinitionQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<EntityDefinitionDto> Handle(GetEntityDefinitionQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        EntityDefinition definition = await provider.GetEntityDefinitionAsync(request.EntityPath, cancellationToken);

        return new EntityDefinitionDto(definition.Name, definition.Kind, definition.ParentPath, definition.Options);
    }
}
