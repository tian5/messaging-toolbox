using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Queries.GetHeldLockStates;

public sealed class GetHeldLockStatesQueryHandler : IRequestHandler<GetHeldLockStatesQuery, IReadOnlyDictionary<string, DateTimeOffset>>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetHeldLockStatesQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyDictionary<string, DateTimeOffset>> Handle(GetHeldLockStatesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        return await provider.GetHeldLockStatesAsync(
            request.EntityPath,
            request.SubQueue,
            request.LockTokens,
            cancellationToken);
    }
}
