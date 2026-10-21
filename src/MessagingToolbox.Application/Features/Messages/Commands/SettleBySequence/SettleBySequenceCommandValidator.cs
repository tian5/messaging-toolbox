using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.SettleBySequence;

public sealed class SettleBySequenceCommandValidator : AbstractValidator<SettleBySequenceCommand>
{
    public SettleBySequenceCommandValidator()
    {
        RuleFor(command => command.SubQueue)
            .Must(subQueue => subQueue is MessageSubQueue.DeadLetter or MessageSubQueue.TransferDeadLetter)
            .WithMessage("Sequence-number settlement is only available on dead-letter sub-queues — on active queues, receive the messages for settlement (lock tokens) instead.");

        RuleFor(command => command.SequenceNumbers)
            .Must(sequenceNumbers => sequenceNumbers.Count > 0)
            .WithMessage("At least one sequence number is required.");

        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage("A reason is required to dead-letter.")
            .When(command => command.Operation == SettlementOperation.DeadLetter);
    }
}
