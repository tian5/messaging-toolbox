using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Explorer.Queries.RefreshCounts;

public sealed class RefreshCountsQueryHandler : IRequestHandler<RefreshCountsQuery, IReadOnlyDictionary<string, EntityRuntimeInfoDto>>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public RefreshCountsQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyDictionary<string, EntityRuntimeInfoDto>> Handle(RefreshCountsQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        Dictionary<string, EntityRuntimeInfoDto> counts = new(StringComparer.Ordinal);
        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(cancellationToken))
        {
            if (entity.RuntimeInfo is not null)
            {
                counts[entity.Path] = new EntityRuntimeInfoDto(
                    entity.RuntimeInfo.ActiveMessageCount,
                    entity.RuntimeInfo.ScheduledMessageCount,
                    entity.RuntimeInfo.DeadLetterMessageCount,
                    entity.RuntimeInfo.TransferDeadLetterMessageCount,
                    entity.RuntimeInfo.SizeInBytes,
                    entity.RuntimeInfo.UpdatedAt,
                    entity.RuntimeInfo.IsApproximate,
                    entity.RuntimeInfo.DeferredMessageCount);
            }
        }

        return counts;
    }
}
