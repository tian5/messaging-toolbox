using FluentValidation;

namespace MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;

public sealed class SaveCannedMessageCommandValidator : AbstractValidator<SaveCannedMessageCommand>
{
    public SaveCannedMessageCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
    }
}
