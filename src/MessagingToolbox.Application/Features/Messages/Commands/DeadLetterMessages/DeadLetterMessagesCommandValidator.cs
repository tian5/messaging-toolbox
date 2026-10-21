using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;

public sealed class DeadLetterMessagesCommandValidator : AbstractValidator<DeadLetterMessagesCommand>
{
    public DeadLetterMessagesCommandValidator()
    {
        RuleFor(command => command.SubQueue)
            .Must(subQueue => subQueue is not MessageSubQueue.DeadLetter and not MessageSubQueue.TransferDeadLetter)
            .WithMessage("Dead-lettered messages cannot be dead-lettered again.");

        RuleFor(command => command)
            .Must(command => command.SubQueue == MessageSubQueue.Scheduled
                ? command.SequenceNumbers.Count > 0
                : command.LockTokens is { Count: > 0 })
            .WithMessage(command => command.SubQueue == MessageSubQueue.Scheduled
                ? "At least one scheduled sequence number is required."
                : "Messages must be received for settlement (lock tokens) before they can be dead-lettered.");
    }
}
