using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;

public sealed class PeekMessagesQueryHandler : IRequestHandler<PeekMessagesQuery, IReadOnlyList<MessageDto>>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public PeekMessagesQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyList<MessageDto>> Handle(PeekMessagesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        PeekRequest peekRequest = new(request.EntityPath, request.SubQueue, request.MaxMessages, request.FromSequenceNumber, request.SessionId);
        IReadOnlyList<ToolboxMessage> messages = await provider.PeekAsync(peekRequest, cancellationToken);

        return messages.Select(MessageDtoMapper.ToDto).ToList();
    }
}
