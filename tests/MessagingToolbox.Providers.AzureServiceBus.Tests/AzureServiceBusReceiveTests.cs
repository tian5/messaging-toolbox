using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusReceiveTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    [Fact]
    public async Task ReceiveAsync_QueueDrainsBeforeBudget_FollowUpBatchUsesShortWait()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceiver receiver = CreateReceiver([[CreateMessage(1), CreateMessage(2)], []]);
        sut.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveAsync(
            new ReceiveRequest("orders", MessageSubQueue.Active, 50, TimeSpan.FromSeconds(10)),
            CancellationToken.None);


        received.Count.ShouldBe(2);
        await receiver.Received(1).ReceiveMessagesAsync(Arg.Any<int>(), TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
        await receiver.Received(1).ReceiveMessagesAsync(Arg.Any<int>(), TimeSpan.FromSeconds(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAsync_EmptyQueue_StopsAfterOneWait()
    {
        await using AzureServiceBusProvider sut = CreateProvider();
        ServiceBusReceiver receiver = CreateReceiver([[]]);
        sut.ReceiverFactory = (_, _, _, _) => receiver;


        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveAsync(
            new ReceiveRequest("orders", MessageSubQueue.Active, 50, TimeSpan.FromSeconds(10)),
            CancellationToken.None);


        received.ShouldBeEmpty();
        await receiver.Received(1).ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).DisposeAsync();
    }

    private static AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            Substitute.For<IAzureServiceBusManagement>());

    private static ServiceBusReceivedMessage CreateMessage(long sequenceNumber)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData($"{{\"seq\": {sequenceNumber}}}"),
            messageId: $"msg-{sequenceNumber}",
            sequenceNumber: sequenceNumber);

    private static ServiceBusReceiver CreateReceiver(IReadOnlyList<IReadOnlyList<ServiceBusReceivedMessage>> batches)
    {
        ServiceBusReceiver receiver = Substitute.For<ServiceBusReceiver>();
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(batches[0], batches.Skip(1).ToArray());
        return receiver;
    }
}
