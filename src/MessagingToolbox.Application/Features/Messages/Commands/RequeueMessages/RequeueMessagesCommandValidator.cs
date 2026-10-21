using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;

public sealed class RequeueMessagesCommandValidator : AbstractValidator<RequeueMessagesCommand>
{
    public RequeueMessagesCommandValidator()
    {
        RuleFor(command => command.SourceSubQueue)
            .Must(subQueue => subQueue is MessageSubQueue.DeadLetter or MessageSubQueue.TransferDeadLetter)
            .WithMessage("Only dead-lettered messages can be requeued.");

        RuleFor(command => command.TargetPath)
            .NotEmpty()
            .WithMessage("A target queue or topic is required.");

        RuleFor(command => command)
            .Must(command => command.Messages.Count > 0)
            .WithMessage("At least one message is required.");
    }
}
