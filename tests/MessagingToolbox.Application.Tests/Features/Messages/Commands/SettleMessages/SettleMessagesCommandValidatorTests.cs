using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.SettleMessages;

public class SettleMessagesCommandValidatorTests
{
    private readonly SettleMessagesCommandValidator _sut = new();

    [Theory]
    [InlineData(MessageSubQueue.DeadLetter)]
    [InlineData(MessageSubQueue.TransferDeadLetter)]
    public void Validate_DeadLetterOperationOnDeadLetterSubQueue_HasValidationErrorForOperation(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new SettleMessagesCommand(Guid.NewGuid(), "orders", subQueue, SettlementOperation.DeadLetter, ["token-1"], null, null))
            .ShouldHaveValidationErrorFor(command => command.Operation);
    }

    [Theory]
    [InlineData(MessageSubQueue.Active)]
    [InlineData(MessageSubQueue.Scheduled)]
    public void Validate_DeadLetterOperationOnOtherSubQueue_DoesNotHaveValidationErrorForOperation(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new SettleMessagesCommand(Guid.NewGuid(), "orders", subQueue, SettlementOperation.DeadLetter, ["token-1"], null, null))
            .ShouldNotHaveValidationErrorFor(command => command.Operation);
    }

    [Theory]
    [InlineData(MessageSubQueue.DeadLetter)]
    [InlineData(MessageSubQueue.TransferDeadLetter)]
    [InlineData(MessageSubQueue.Active)]
    public void Validate_CompleteOperation_DoesNotHaveValidationErrorForOperation(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new SettleMessagesCommand(Guid.NewGuid(), "orders", subQueue, SettlementOperation.Complete, ["token-1"], null, null))
            .ShouldNotHaveValidationErrorFor(command => command.Operation);
    }
}
