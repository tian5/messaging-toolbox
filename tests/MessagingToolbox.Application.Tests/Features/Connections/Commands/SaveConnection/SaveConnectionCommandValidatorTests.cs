using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Features.Connections.Commands.SaveConnection;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Connections.Commands.SaveConnection;

public class SaveConnectionCommandValidatorTests
{
    private readonly SaveConnectionCommandValidator _sut = new();

    [Fact]
    public void Validate_NewConnectionWithoutDisplayName_HasValidationErrorForDisplayName()
    {
        _sut.TestValidate(new SaveConnectionCommand(null, string.Empty, "Endpoint=sb://localhost;...", ProviderKind.AzureServiceBus))
            .ShouldHaveValidationErrorFor(command => command.DisplayName);
    }

    [Fact]
    public void Validate_NewConnectionWithoutConnectionString_HasValidationErrorForConnectionString()
    {
        _sut.TestValidate(new SaveConnectionCommand(null, "Local emulator", string.Empty, ProviderKind.AzureServiceBus))
            .ShouldHaveValidationErrorFor(command => command.ConnectionString);
    }

    [Fact]
    public void Validate_ExistingConnectionWithoutConnectionString_DoesNotHaveValidationErrorForConnectionString()
    {
        _sut.TestValidate(new SaveConnectionCommand(Guid.NewGuid(), "Local emulator", string.Empty, ProviderKind.AzureServiceBus))
            .ShouldNotHaveValidationErrorFor(command => command.ConnectionString);
    }

    [Fact]
    public void Validate_NewConnectionWithInvalidKind_HasValidationErrorForKind()
    {
        _sut.TestValidate(new SaveConnectionCommand(null, "Local emulator", "Endpoint=sb://localhost;...", (ProviderKind)999))
            .ShouldHaveValidationErrorFor(command => command.Kind);
    }
}
