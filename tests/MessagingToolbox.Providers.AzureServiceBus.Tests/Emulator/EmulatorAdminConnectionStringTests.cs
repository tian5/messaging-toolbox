using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Emulator;

public class EmulatorAdminConnectionStringTests
{
    [Theory]
    [InlineData(
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true",
        "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true")]
    [InlineData(
        "Endpoint=sb://host.example.com:5672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true",
        "Endpoint=sb://host.example.com:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true")]
    [InlineData(
        "UseDevelopmentEmulator=true;Endpoint=sb://127.0.0.1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake",
        "UseDevelopmentEmulator=true;Endpoint=sb://127.0.0.1:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake")]
    public void FromRuntimeConnectionString_WithEndpoint_RewritesPortTo5300(string input, string expected)
    {
        string? result = EmulatorAdminConnectionString.FromRuntimeConnectionString(input);

        result.ShouldBe(expected);
    }

    [Fact]
    public void FromRuntimeConnectionString_WithoutEndpoint_ReturnsNull()
    {
        string? result = EmulatorAdminConnectionString.FromRuntimeConnectionString(
            "SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true");

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://localhost:51234", "localhost:51234")]
    [InlineData("http://localhost:51234/", "localhost:51234")]
    [InlineData("localhost:51234", "localhost:51234")]
    [InlineData("tcp://servicebus-emulator:5300", "servicebus-emulator:5300")]
    public void FromRuntimeConnectionString_WithAdminEndpoint_UsesItsHostAndPort(string adminEndpoint, string expectedAuthority)
    {
        string? result = EmulatorAdminConnectionString.FromRuntimeConnectionString(
            "Endpoint=sb://localhost:60001;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true",
            adminEndpoint);


        result.ShouldBe($"Endpoint=sb://{expectedAuthority};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("::not a uri::")]
    public void FromRuntimeConnectionString_WithoutUsableAdminEndpoint_FallsBackToPort5300(string? adminEndpoint)
    {
        string? result = EmulatorAdminConnectionString.FromRuntimeConnectionString(
            "Endpoint=sb://localhost:60001;SharedAccessKey=fake;UseDevelopmentEmulator=true",
            adminEndpoint);


        result.ShouldBe("Endpoint=sb://localhost:5300;SharedAccessKey=fake;UseDevelopmentEmulator=true");
    }
}
