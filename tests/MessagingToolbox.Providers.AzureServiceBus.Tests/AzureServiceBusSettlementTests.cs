using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusSettlementTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    [Fact]
    public async Task ReceiveForSettlementAsync_ScheduledSubQueue_ThrowsNotSupported()
    {
        await using AzureServiceBusProvider provider = CreateProvider();


        NotSupportedByProviderException exception = await Should.ThrowAsync<NotSupportedByProviderException>(
            () => provider.ReceiveForSettlementAsync("orders", MessageSubQueue.Scheduled, 10, null, CancellationToken.None));


        exception.Message.ShouldContain("scheduled");
    }

    [Fact]
    public async Task SettleAsync_UnknownLockToken_ReturnsItAsNotSettled()
    {
        await using AzureServiceBusProvider provider = CreateProvider();


        IReadOnlyList<string> settled = await provider.SettleAsync(
            new SettleRequest("orders", MessageSubQueue.Active, SettlementOperation.Complete, ["no-such-token"], null, null),
            CancellationToken.None);


        settled.ShouldBeEmpty();
    }

    private static AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            Substitute.For<IAzureServiceBusManagement>());
}
