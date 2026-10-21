using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Queries.GetPurgePreview;

public sealed class GetPurgePreviewQueryHandler : IRequestHandler<GetPurgePreviewQuery, long?>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetPurgePreviewQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<long?> Handle(GetPurgePreviewQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        EntityRuntimeInfo info;
        try
        {
            info = await provider.GetRuntimeInfoAsync(request.EntityPath, cancellationToken);
        }
        catch (NotSupportedByProviderException)
        {
            return null;
        }

        return request.SubQueue switch
        {
            MessageSubQueue.DeadLetter => info.DeadLetterMessageCount,
            MessageSubQueue.TransferDeadLetter => info.TransferDeadLetterMessageCount,
            _ => info.ActiveMessageCount,
        };
    }
}
