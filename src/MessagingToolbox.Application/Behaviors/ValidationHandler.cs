using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace MessagingToolbox.Application.Behaviors;

public sealed class ValidationHandler<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    private readonly IEnumerable<IValidator<TMessage>> _validators;

    public ValidationHandler(IEnumerable<IValidator<TMessage>> validators)
    {
        _validators = validators;
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            ValidationContext<TMessage> context = new(message);
            ValidationResult[] results = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            List<ValidationFailure> failures = results
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
            {
                throw new ValidationException(failures);
            }
        }
        
        return await next(message, cancellationToken);
    }
}
