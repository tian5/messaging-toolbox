using Mediator;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Behaviors;

public sealed class ErrorLoggingHandler<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage: IMessage
{
    private readonly ILogger<ErrorLoggingHandler<TMessage, TResponse>> _logger;

    public ErrorLoggingHandler(ILogger<ErrorLoggingHandler<TMessage, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            TResponse response = await next(message, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while handling {Message}.", typeof(TMessage).Name);
            throw;
        }
    }
}
