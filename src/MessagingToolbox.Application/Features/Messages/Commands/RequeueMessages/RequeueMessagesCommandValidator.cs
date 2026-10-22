using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;

public sealed class RequeueMessagesCommandValidator : AbstractValidator<RequeueMessagesCommand>
{
    public RequeueMessagesCommandValidator()
    {
        // Which sources may be requeued depends on the broker (dead-letter sub-queue or not), so
        // the handler checks that against the provider's capabilities.
        RuleFor(command => command.SourceSubQueue)
            .NotEqual(MessageSubQueue.Scheduled)
            .WithMessage("Scheduled messages can't be requeued.");

        RuleFor(command => command.TargetPath)
            .NotEmpty()
            .WithMessage("A target queue or topic is required.");

        RuleFor(command => command)
            .Must(command => command.Messages.Count > 0)
            .WithMessage("At least one message is required.");
    }
}
