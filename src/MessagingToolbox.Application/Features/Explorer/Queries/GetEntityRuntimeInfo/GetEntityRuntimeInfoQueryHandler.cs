using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Explorer.Queries.GetEntityRuntimeInfo;

public sealed class GetEntityRuntimeInfoQueryHandler : IRequestHandler<GetEntityRuntimeInfoQuery, EntityRuntimeInfoDto?>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetEntityRuntimeInfoQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<EntityRuntimeInfoDto?> Handle(GetEntityRuntimeInfoQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        EntityRuntimeInfo info;
        try
        {
            info = await provider.GetRuntimeInfoAsync(request.EntityPath, cancellationToken);
        }
        catch (NotSupportedByProviderException)
        {
            // Expected on connections without a management plane (e.g. the Azure Service Bus
            // emulator) — the UI renders counts as unavailable instead of an error.
            return null;
        }

        return new EntityRuntimeInfoDto(
            info.ActiveMessageCount,
            info.ScheduledMessageCount,
            info.DeadLetterMessageCount,
            info.TransferDeadLetterMessageCount,
            info.SizeInBytes,
            info.UpdatedAt,
            info.IsApproximate,
            info.DeferredMessageCount);
    }
}
