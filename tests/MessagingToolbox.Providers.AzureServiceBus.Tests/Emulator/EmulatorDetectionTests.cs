using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Emulator;

public class EmulatorDetectionTests
{
    [Theory]
    [InlineData("Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true")]
    [InlineData("Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;usedevelopmentemulator=TRUE")]
    [InlineData("Endpoint=sb://localhost; UseDevelopmentEmulator = true ;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake")]
    public void IsEmulatorConnectionString_EmulatorFlag_ReturnsTrue(string connectionString)
    {
        EmulatorDetection.IsEmulatorConnectionString(connectionString).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Endpoint=sb://namespace.servicebus.windows.net;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake")]
    [InlineData("Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=false")]
    [InlineData("Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;NotUseDevelopmentEmulator=true")]
    [InlineData(null)]
    [InlineData("")]
    public void IsEmulatorConnectionString_CloudOrEmpty_ReturnsFalse(string? connectionString)
    {
        EmulatorDetection.IsEmulatorConnectionString(connectionString).ShouldBeFalse();
    }
}
