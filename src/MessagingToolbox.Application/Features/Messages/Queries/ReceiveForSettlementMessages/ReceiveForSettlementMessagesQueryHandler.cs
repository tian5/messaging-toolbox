using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveForSettlementMessages;

public sealed class ReceiveForSettlementMessagesQueryHandler : IRequestHandler<ReceiveForSettlementMessagesQuery, IReadOnlyList<MessageDto>>
{
    private static readonly TimeSpan _defaultMaxWaitTime = TimeSpan.FromSeconds(10);

    private readonly IMessagingProviderFactory _providerFactory;

    public ReceiveForSettlementMessagesQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyList<MessageDto>> Handle(ReceiveForSettlementMessagesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        if (!provider.Capabilities.CanReceiveSettle)
        {
            throw new NotSupportedByProviderException("This connection does not support receiving messages for settlement.");
        }

        if (request.SessionId is not null && !provider.Capabilities.SupportsSessions)
        {
            throw new NotSupportedByProviderException("This connection does not support sessions.");
        }

        IReadOnlyList<ToolboxMessage> messages = await provider.ReceiveForSettlementAsync(
            request.EntityPath,
            request.SubQueue,
            request.MaxMessages,
            request.MaxWaitTime ?? _defaultMaxWaitTime,
            cancellationToken,
            request.LockRenewalSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            request.SessionId);

        // Not audited: unlike receive-and-delete, nothing is deleted here.
        return messages.Select(MessageDtoMapper.ToDto).ToList();
    }
}
