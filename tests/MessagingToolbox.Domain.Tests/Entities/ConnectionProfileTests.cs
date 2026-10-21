using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Domain.Entities;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Domain.Tests.Entities;

public class ConnectionProfileTests
{
    [Fact]
    public void Constructor_SetsKindToAzureServiceBus()
    {
        ConnectionProfile profile = new();


        profile.Kind.ShouldBe(ProviderKind.AzureServiceBus);
    }

    [Fact]
    public void Constructor_SetsEmptyProtectedConnectionString()
    {
        ConnectionProfile profile = new();


        profile.ProtectedConnectionString.ShouldBe(string.Empty);
    }
}
