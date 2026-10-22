using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;

public sealed class ReceiveDeferredMessagesQueryHandler : IRequestHandler<ReceiveDeferredMessagesQuery, IReadOnlyList<MessageDto>>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public ReceiveDeferredMessagesQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyList<MessageDto>> Handle(ReceiveDeferredMessagesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        if (!provider.Capabilities.CanReceiveSettle)
        {
            throw new NotSupportedByProviderException("This connection does not support receiving messages for settlement.");
        }

        if (!provider.Capabilities.SupportsDeferral)
        {
            throw new NotSupportedByProviderException("This connection does not support deferred messages.");
        }

        IReadOnlyList<ToolboxMessage> messages = await provider.ReceiveDeferredForSettlementAsync(
            new ReceiveDeferredRequest(
                request.EntityPath,
                request.SubQueue,
                request.Messages,
                request.LockRenewalSeconds is { } seconds and > 0 ? TimeSpan.FromSeconds(seconds) : null),
            cancellationToken);

        // Not audited: like a settle-later receive, nothing is deleted here.
        return messages.Select(MessageDtoMapper.ToDto).ToList();
    }
}
