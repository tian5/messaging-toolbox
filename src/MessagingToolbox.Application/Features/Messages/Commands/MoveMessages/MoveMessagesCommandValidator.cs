using FluentValidation;

namespace MessagingToolbox.Application.Features.Messages.Commands.MoveMessages;

public sealed class MoveMessagesCommandValidator : AbstractValidator<MoveMessagesCommand>
{
    public MoveMessagesCommandValidator()
    {
        RuleFor(command => command.ConnectionId).NotEmpty();
        RuleFor(command => command.SourcePath).NotEmpty().MaximumLength(260);
        RuleFor(command => command.DestinationPath)
            .NotEmpty()
            .MaximumLength(260)
            .NotEqual(command => command.SourcePath)
            .WithMessage("Choose a destination other than the source.");
    }
}
