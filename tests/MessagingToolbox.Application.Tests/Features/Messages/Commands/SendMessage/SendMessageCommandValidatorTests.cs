using FluentValidation.TestHelper;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Features.Messages.Commands.SendMessage;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.SendMessage;

public class SendMessageCommandValidatorTests
{
    private readonly SendMessageCommandValidator _sut = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Validate_InvalidCopiesCount_HasValidationErrorForCopies(int copies)
    {
        _sut.TestValidate(new SendMessageCommand(
            Guid.NewGuid(), "orders", "body", null, new Dictionary<string, string>(),
            null, null, null, null, null, null, null, null, copies))
            .ShouldHaveValidationErrorFor(command => command.Copies);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void Validate_ValidCopiesCount_DoesNotHaveValidationErrorForCopies(int copies)
    {
        _sut.TestValidate(new SendMessageCommand(
            Guid.NewGuid(), "orders", "body", null, new Dictionary<string, string>(),
            null, null, null, null, null, null, null, null, copies))
            .ShouldNotHaveValidationErrorFor(command => command.Copies);
    }

    [Fact]
    public void Validate_TypedPropertyNotOfItsType_HasValidationError()
    {
        _sut.TestValidate(new SendMessageCommand(
            Guid.NewGuid(), "orders", "body", null, new Dictionary<string, string> { ["count"] = "many" },
            null, null, null, null, null, null, null, null, 1,
            new Dictionary<string, PropertyValueType> { ["count"] = PropertyValueType.Int32 }))
            .Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe("Application property 'count': 'many' is not a valid Int32 value.");
    }

    [Theory]
    [InlineData("7")]
    [InlineData("~randomInt(1,9)~")]
    public void Validate_TypedPropertyLiteralOrTemplate_IsValid(string value)
    {
        _sut.TestValidate(new SendMessageCommand(
            Guid.NewGuid(), "orders", "body", null, new Dictionary<string, string> { ["count"] = value },
            null, null, null, null, null, null, null, null, 1,
            new Dictionary<string, PropertyValueType> { ["count"] = PropertyValueType.Int32 }))
            .ShouldNotHaveAnyValidationErrors();
    }
}
