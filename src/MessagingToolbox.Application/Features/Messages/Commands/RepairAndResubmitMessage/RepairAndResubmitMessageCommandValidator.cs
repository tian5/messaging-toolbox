using FluentValidation;
using MessagingToolbox.Application.Common;

namespace MessagingToolbox.Application.Features.Messages.Commands.RepairAndResubmitMessage;

public sealed class RepairAndResubmitMessageCommandValidator : AbstractValidator<RepairAndResubmitMessageCommand>
{
    public RepairAndResubmitMessageCommandValidator()
    {
        RuleFor(command => command.TargetPath).NotEmpty();

        RuleForEach(command => command.ApplicationProperties)
            .Must((command, property) => ApplicationPropertyValues.IsValidLiteral(property.Value, TypeOf(command, property.Key)))
            .WithMessage((command, property) => $"Application property '{property.Key}': '{property.Value}' is not a valid {TypeOf(command, property.Key)} value.");
    }

    private static PropertyValueType TypeOf(RepairAndResubmitMessageCommand command, string key)
        => command.PropertyTypes?.GetValueOrDefault(key) ?? PropertyValueType.String;
}
