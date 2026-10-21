using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.DeleteMessages;

public sealed class DeleteMessagesCommandValidator : AbstractValidator<DeleteMessagesCommand>
{
    public DeleteMessagesCommandValidator()
    {
        RuleFor(command => command)
            .Must(command => command.SubQueue == MessageSubQueue.Scheduled
                ? command.SequenceNumbers.Count > 0
                : command.LockTokens is { Count: > 0 })
            .WithMessage(command => command.SubQueue == MessageSubQueue.Scheduled
                ? "At least one scheduled sequence number is required."
                : "Messages must be received for settlement (lock tokens) before they can be deleted.");
    }
}
