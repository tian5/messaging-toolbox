using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;

public sealed class SettleMessagesCommandValidator : AbstractValidator<SettleMessagesCommand>
{
    public SettleMessagesCommandValidator()
    {
        RuleFor(command => command.Operation)
            .Must((command, operation) => operation != SettlementOperation.DeadLetter
                || command.SubQueue is not MessageSubQueue.DeadLetter and not MessageSubQueue.TransferDeadLetter)
            .WithMessage("Dead-lettered messages cannot be dead-lettered again.");
    }
}
