using System.Text;
using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Messaging;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests;

public class AzureServiceBusMessageMapperTests
{
    [Fact]
    public void ToToolboxMessage_ReceivedMessage_MapsSystemAndApplicationProperties()
    {
        DateTimeOffset enqueued = new(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
        ServiceBusReceivedMessage received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("{\"orderId\": 7}")),
            messageId: "msg-1",
            sessionId: "session-42",
            correlationId: "corr-1",
            subject: "order-created",
            to: "orders",
            contentType: "application/json",
            replyTo: "replies",
            replyToSessionId: "reply-session-1",
            partitionKey: "pk-1",
            deliveryCount: 3,
            sequenceNumber: 123,
            enqueuedTime: enqueued,
            properties: new Dictionary<string, object> { ["tenant"] = "tests" });


        ToolboxMessage message = AzureServiceBusMessageMapper.ToToolboxMessage(received);


        message.MessageId.ShouldBe("msg-1");
        message.SessionId.ShouldBe("session-42");
        message.CorrelationId.ShouldBe("corr-1");
        message.Subject.ShouldBe("order-created");
        message.To.ShouldBe("orders");
        message.ReplyTo.ShouldBe("replies");
        message.ReplyToSessionId.ShouldBe("reply-session-1");
        message.PartitionKey.ShouldBe("pk-1");
        message.ContentType.ShouldBe("application/json");
        message.DeliveryCount.ShouldBe(3);
        message.SequenceNumber.ShouldBe(123);
        message.EnqueuedTime.ShouldBe(enqueued);
        Encoding.UTF8.GetString(message.Body).ShouldBe("{\"orderId\": 7}");
        message.ApplicationProperties["tenant"].ShouldBe("tests");
    }

    [Fact]
    public void ToServiceBusMessage_ToolboxMessage_MapsEditableFieldsAndSkipsNullProperties()
    {
        ToolboxMessage message = new()
        {
            Body = Encoding.UTF8.GetBytes("body"),
            ContentType = "text/plain",
            Subject = "hello",
            SessionId = "session-1",
            ReplyToSessionId = "reply-session-1",
            PartitionKey = "session-1",
            ApplicationProperties = new Dictionary<string, object?>
            {
                ["tenant"] = "tests",
                ["unset"] = null,
            },
        };


        ServiceBusMessage serviceBusMessage = AzureServiceBusMessageMapper.ToServiceBusMessage(message);


        serviceBusMessage.Body.ToString().ShouldBe("body");
        serviceBusMessage.ContentType.ShouldBe("text/plain");
        serviceBusMessage.Subject.ShouldBe("hello");
        serviceBusMessage.SessionId.ShouldBe("session-1");
        serviceBusMessage.ReplyToSessionId.ShouldBe("reply-session-1");
        serviceBusMessage.PartitionKey.ShouldBe("session-1");
        serviceBusMessage.ApplicationProperties["tenant"].ShouldBe("tests");
        serviceBusMessage.ApplicationProperties.ShouldNotContainKey("unset");
    }

    [Fact]
    public void ToToolboxMessage_EmptyLockToken_ReturnsNull()
    {
        ServiceBusReceivedMessage peeked = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("peeked"),
            sequenceNumber: 1);


        ToolboxMessage message = AzureServiceBusMessageMapper.ToToolboxMessage(peeked);


        message.LockToken.ShouldBeNull();
    }

    [Fact]
    public void ToToolboxMessage_RealLockToken_ReturnsLockTokenString()
    {
        Guid lockToken = Guid.NewGuid();
        ServiceBusReceivedMessage received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("locked"),
            sequenceNumber: 2,
            lockTokenGuid: lockToken);


        ToolboxMessage message = AzureServiceBusMessageMapper.ToToolboxMessage(received);


        message.LockToken.ShouldBe(lockToken.ToString());
    }

    [Theory]
    [InlineData(ServiceBusMessageState.Active, MessageState.Active)]
    [InlineData(ServiceBusMessageState.Deferred, MessageState.Deferred)]
    [InlineData(ServiceBusMessageState.Scheduled, MessageState.Scheduled)]
    public void ToToolboxMessage_MessageState_MapsState(ServiceBusMessageState brokerState, MessageState expected)
    {
        ServiceBusReceivedMessage peeked = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("peeked"),
            sequenceNumber: 3,
            serviceBusMessageState: brokerState);


        ToolboxMessage message = AzureServiceBusMessageMapper.ToToolboxMessage(peeked);


        message.State.ShouldBe(expected);
    }
}
