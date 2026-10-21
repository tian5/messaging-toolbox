using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Commands.SendMessage;

public sealed class SendMessageCommandHandler : IRequestHandler<SendMessageCommand>
{
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessageTemplateEngine _templateEngine;

    public SendMessageCommandHandler(IMessagingProviderFactory providerFactory, IMessageTemplateEngine templateEngine)
    {
        _providerFactory = providerFactory;
        _templateEngine = templateEngine;
    }

    public async ValueTask<Unit> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        // Templating renders per copy, so ~newGuid~ / ~randomInt~ produce distinct messages.
        List<ToolboxMessage> messages = [];
        for (int copy = 0; copy < request.Copies; copy++)
        {
            messages.Add(MessageDtoMapper.ToToolboxMessage(
                _templateEngine.Render(request.Body),
                request.ContentType,
                ApplicationPropertyValues.ToTyped(request.ApplicationProperties, request.PropertyTypes, _templateEngine.Render),
                subject: request.Subject,
                messageId: request.MessageId,
                correlationId: request.CorrelationId,
                sessionId: request.SessionId,
                to: request.To,
                replyTo: request.ReplyTo,
                scheduledEnqueueTime: request.ScheduledEnqueueTime,
                timeToLive: request.TimeToLive));
        }

        await provider.SendAsync(request.EntityPath, messages, cancellationToken);

        return Unit.Value;
    }
}
