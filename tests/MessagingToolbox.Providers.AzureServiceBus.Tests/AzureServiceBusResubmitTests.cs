using System.Text;
using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusResubmitTests
{
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true";

    [Fact]
    public async Task PrepareResubmitAsync_CacheMiss_FallsBackToMapperAndAddsReplayProperty()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ToolboxMessage original = new()
        {
            Body = Encoding.UTF8.GetBytes("body"),
            MessageId = "msg-1",
            Subject = "order-created",
            SequenceNumber = 7,
        };


        ToolboxMessage clone = await provider.PrepareResubmitAsync(
            "orders", MessageSubQueue.Active, original, ResubmitOverrides.Empty, CancellationToken.None);


        clone.MessageId.ShouldNotBeNull();
        clone.Subject.ShouldBe("order-created");
        clone.SequenceNumber.ShouldBeNull();
        clone.LockToken.ShouldBeNull();
        clone.ApplicationProperties["toolbox-replayed-from"].ShouldBe("msg-1");
    }

    [Fact]
    public async Task PrepareResubmitAsync_CacheMiss_GeneratesNewMessageIdWhenNull()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ToolboxMessage original = new()
        {
            Body = Encoding.UTF8.GetBytes("body"),
            SequenceNumber = 7,
        };


        ToolboxMessage clone = await provider.PrepareResubmitAsync(
            "orders", MessageSubQueue.Active, original, ResubmitOverrides.Empty, CancellationToken.None);


        clone.MessageId.ShouldNotBeNullOrEmpty();
        clone.ApplicationProperties["toolbox-replayed-from"].ShouldBe("7");
    }

    [Fact]
    public async Task PrepareResubmitAsync_CacheHit_ClonesSdkMessageAndPreservesPartitionKeyAndReplyToSessionId()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ServiceBusReceivedMessage sdkOriginal = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            messageId: "msg-1",
            sessionId: "session-1",
            replyToSessionId: "reply-session-1",
            partitionKey: "pk-1",
            sequenceNumber: 42);

        provider.CacheOriginalForTest("orders", MessageSubQueue.Active, sdkOriginal);

        ToolboxMessage original = AzureServiceBusMessageMapper.ToToolboxMessage(sdkOriginal);


        ToolboxMessage clone = await provider.PrepareResubmitAsync(
            "orders", MessageSubQueue.Active, original, ResubmitOverrides.Empty, CancellationToken.None);


        Encoding.UTF8.GetString(clone.Body).ShouldBe("payload");
        clone.MessageId.ShouldBe("msg-1");
        clone.SessionId.ShouldBe("session-1");
        clone.ReplyToSessionId.ShouldBe("reply-session-1");
        clone.PartitionKey.ShouldBe("pk-1");
        clone.SequenceNumber.ShouldBeNull();
        clone.ApplicationProperties["toolbox-replayed-from"].ShouldBe("msg-1");
    }

    [Fact]
    public async Task PrepareResubmitAsync_Overrides_AreAppliedAndMessageIdCanBePreserved()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ToolboxMessage original = new()
        {
            Body = Encoding.UTF8.GetBytes("body"),
            MessageId = "msg-1",
            Subject = "original",
            SequenceNumber = 7,
        };

        ResubmitOverrides overrides = new(
            Subject: "edited",
            MessageId: "msg-2");


        ToolboxMessage clone = await provider.PrepareResubmitAsync(
            "orders", MessageSubQueue.Active, original, overrides, CancellationToken.None);


        clone.Subject.ShouldBe("edited");
        clone.MessageId.ShouldBe("msg-2");
        clone.ApplicationProperties["toolbox-replayed-from"].ShouldBe("msg-1");
    }

    [Fact]
    public async Task PrepareResubmitAsync_ApplicationPropertiesOverride_ReplacesEntireDictionary()
    {
        await using AzureServiceBusProvider provider = CreateProvider();
        ToolboxMessage original = new()
        {
            Body = Encoding.UTF8.GetBytes("body"),
            MessageId = "msg-1",
            SequenceNumber = 7,
            ApplicationProperties = new Dictionary<string, object?>
            {
                ["keep"] = "value",
                ["remove"] = "gone",
            },
        };

        ResubmitOverrides overrides = new(
            ApplicationProperties: new Dictionary<string, object?> { ["new"] = "value" });


        ToolboxMessage clone = await provider.PrepareResubmitAsync(
            "orders", MessageSubQueue.Active, original, overrides, CancellationToken.None);


        clone.ApplicationProperties.ShouldContainKey("new");
        clone.ApplicationProperties["new"].ShouldBe("value");
        clone.ApplicationProperties.ShouldNotContainKey("keep");
        clone.ApplicationProperties.ShouldNotContainKey("remove");
        clone.ApplicationProperties["toolbox-replayed-from"].ShouldBe("msg-1");
    }

    private static AzureServiceBusProvider CreateProvider()
        => new(
            EmulatorConnectionString,
            [],
            NullLogger<AzureServiceBusProvider>.Instance,
            TimeProvider.System,
            Substitute.For<IAzureServiceBusManagement>());
}
