using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.DeadLetterMessages;

public class DeadLetterMessagesCommandValidatorTests
{
    private readonly DeadLetterMessagesCommandValidator _sut = new();

    [Fact]
    public void Validate_DeadLetterSubQueue_HasValidationErrorForSubQueue()
    {
        _sut.TestValidate(new DeadLetterMessagesCommand(Guid.NewGuid(), "orders", MessageSubQueue.DeadLetter, [7]))
            .ShouldHaveValidationErrorFor(command => command.SubQueue);
    }

    [Fact]
    public void Validate_TransferDeadLetterSubQueue_HasValidationErrorForSubQueue()
    {
        _sut.TestValidate(new DeadLetterMessagesCommand(Guid.NewGuid(), "orders", MessageSubQueue.TransferDeadLetter, [7]))
            .ShouldHaveValidationErrorFor(command => command.SubQueue);
    }

    [Theory]
    [InlineData(MessageSubQueue.Active)]
    [InlineData(MessageSubQueue.Scheduled)]
    public void Validate_ValidSubQueue_DoesNotHaveValidationErrorForSubQueue(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new DeadLetterMessagesCommand(Guid.NewGuid(), "orders", subQueue, [7]))
            .ShouldNotHaveValidationErrorFor(command => command.SubQueue);
    }
}
