using FluentValidation.TestHelper;
using MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Templates.Commands.SaveCannedMessage;

public class SaveCannedMessageCommandValidatorTests
{
    private readonly SaveCannedMessageCommandValidator _sut = new();

    [Fact]
    public void Validate_EmptyName_HasValidationErrorForName()
    {
        _sut.TestValidate(new SaveCannedMessageCommand(null, string.Empty, "Folder", "body", null, new Dictionary<string, string>()))
            .ShouldHaveValidationErrorFor(command => command.Name);
    }
}
