using FluentValidation.TestHelper;
using MessagingToolbox.Application.Features.Messages.Commands.MoveMessages;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.MoveMessages;

public class MoveMessagesCommandValidatorTests
{
    private readonly MoveMessagesCommandValidator _sut = new();

    [Fact]
    public void Validate_DestinationIsTheSource_HasValidationErrorForDestination()
    {
        _sut.TestValidate(new MoveMessagesCommand(Guid.NewGuid(), "queues/orders", "queues/orders"))
            .ShouldHaveValidationErrorFor(command => command.DestinationPath);
    }

    [Fact]
    public void Validate_OtherDestination_IsValid()
    {
        _sut.TestValidate(new MoveMessagesCommand(Guid.NewGuid(), "queues/orders.dlq", "queues/orders"))
            .ShouldNotHaveAnyValidationErrors();
    }
}
