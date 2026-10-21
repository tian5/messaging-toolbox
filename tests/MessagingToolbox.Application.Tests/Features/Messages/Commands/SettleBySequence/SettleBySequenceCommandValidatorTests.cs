using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Features.Messages.Commands.SettleBySequence;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.SettleBySequence;

public class SettleBySequenceCommandValidatorTests
{
    private readonly SettleBySequenceCommandValidator _sut = new();

    [Theory]
    [InlineData(MessageSubQueue.Active)]
    [InlineData(MessageSubQueue.Scheduled)]
    public void Validate_NonDeadLetterSubQueue_HasValidationErrorForSubQueue(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new SettleBySequenceCommand(Guid.NewGuid(), "orders/$deadletter", subQueue, SettlementOperation.Complete, [7]))
            .ShouldHaveValidationErrorFor(command => command.SubQueue);
    }

    [Theory]
    [InlineData(MessageSubQueue.DeadLetter)]
    [InlineData(MessageSubQueue.TransferDeadLetter)]
    public void Validate_DeadLetterSubQueue_DoesNotHaveValidationErrorForSubQueue(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new SettleBySequenceCommand(Guid.NewGuid(), "orders/$deadletter", subQueue, SettlementOperation.Complete, [7]))
            .ShouldNotHaveValidationErrorFor(command => command.SubQueue);
    }

    [Fact]
    public void Validate_EmptySequenceNumbers_HasValidationErrorForSequenceNumbers()
    {
        _sut.TestValidate(new SettleBySequenceCommand(Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, []))
            .ShouldHaveValidationErrorFor(command => command.SequenceNumbers);
    }

    [Fact]
    public void Validate_DeadLetterOperationWithoutReason_HasValidationErrorForReason()
    {
        _sut.TestValidate(new SettleBySequenceCommand(Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.DeadLetter, [7]))
            .ShouldHaveValidationErrorFor(command => command.Reason);
    }

    [Fact]
    public void Validate_DeadLetterOperationWithReason_DoesNotHaveValidationErrorForReason()
    {
        _sut.TestValidate(new SettleBySequenceCommand(Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.DeadLetter, [7], "poison"))
            .ShouldNotHaveValidationErrorFor(command => command.Reason);
    }

    [Fact]
    public void Validate_CompleteOperationWithoutReason_DoesNotHaveValidationErrorForReason()
    {
        _sut.TestValidate(new SettleBySequenceCommand(Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, SettlementOperation.Complete, [7]))
            .ShouldNotHaveValidationErrorFor(command => command.Reason);
    }
}
