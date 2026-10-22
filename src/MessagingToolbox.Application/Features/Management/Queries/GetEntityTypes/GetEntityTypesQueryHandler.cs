using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Management.Queries.GetEntityTypes;

public sealed class GetEntityTypesQueryHandler : IRequestHandler<GetEntityTypesQuery, IReadOnlyList<EntityTypeDescriptor>>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetEntityTypesQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyList<EntityTypeDescriptor>> Handle(GetEntityTypesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        // Management can be switched off for the connection (or not probed yet): nothing to create then.
        return provider.Capabilities.CanManageEntities ? provider.GetEntityTypes() : [];
    }
}
