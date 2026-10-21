using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusSendTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    private readonly List<int> _sentBatchSizes = [];

    [Fact]
    public async Task SendAsync_MoreMessagesThanOneBatchHolds_SendsSeveralBatches()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.SenderFactory = _ => CreateSender(messagesPerBatch: 2);


        await sut.SendAsync("orders", [CreateMessage(), CreateMessage(), CreateMessage()], CancellationToken.None);


        _sentBatchSizes.ShouldBe([2, 1]);
    }

    [Fact]
    public async Task SendAsync_MessageFitsNoBatch_ThrowsProviderExceptionWithoutSending()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        sut.SenderFactory = _ => CreateSender(messagesPerBatch: 0);


        await Should.ThrowAsync<MessagingProviderException>(
            () => sut.SendAsync("orders", [CreateMessage()], CancellationToken.None));


        _sentBatchSizes.ShouldBeEmpty();
    }

    private ServiceBusSender CreateSender(int messagesPerBatch)
    {
        ServiceBusSender sender = Substitute.For<ServiceBusSender>();
        sender.CreateMessageBatchAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            List<ServiceBusMessage> store = [];
            return new ValueTask<ServiceBusMessageBatch>(ServiceBusModelFactory.ServiceBusMessageBatch(
                256 * 1024,
                store,
                tryAddCallback: _ => store.Count < messagesPerBatch));
        });
        sender.When(substitute => substitute.SendMessagesAsync(Arg.Any<ServiceBusMessageBatch>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => _sentBatchSizes.Add(callInfo.Arg<ServiceBusMessageBatch>().Count));
        return sender;
    }

    private static ToolboxMessage CreateMessage() => new() { Body = "{}"u8.ToArray() };

    private static AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            Substitute.For<IAzureServiceBusManagement>());
}
