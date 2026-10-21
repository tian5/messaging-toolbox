using FluentValidation;
using MessagingToolbox.Application.Common;

namespace MessagingToolbox.Application.Features.Messages.Commands.SendMessage;

public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(command => command.Copies).InclusiveBetween(1, 1000);

        RuleForEach(command => command.ApplicationProperties)
            .Must((command, property) => ApplicationPropertyValues.IsValidLiteral(property.Value, TypeOf(command, property.Key)))
            .WithMessage((command, property) => $"Application property '{property.Key}': '{property.Value}' is not a valid {TypeOf(command, property.Key)} value.");
    }

    private static PropertyValueType TypeOf(SendMessageCommand command, string key)
        => command.PropertyTypes?.GetValueOrDefault(key) ?? PropertyValueType.String;
}
