using System.Text;
using MessagingToolbox.Abstractions.Messaging;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqMessageMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToToolboxMessage_DeadLetteredQuorumMessage_SplitsBrokerHeadersFromApplicationProperties()
    {
        BasicProperties properties = new()
        {
            MessageId = "m-1",
            CorrelationId = "c-1",
            ContentType = "application/json",
            Type = "OrderPlaced",
            Expiration = "60000",
            Timestamp = new AmqpTimestamp(Now.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["tenant"] = Encoding.UTF8.GetBytes("contoso"),
                ["attempt"] = 3,
                ["x-delivery-count"] = 2L,
                ["x-first-death-reason"] = Encoding.UTF8.GetBytes("rejected"),
                ["x-first-death-queue"] = Encoding.UTF8.GetBytes("orders"),
                ["x-death"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["queue"] = Encoding.UTF8.GetBytes("orders"), ["count"] = 1L },
                },
            },
        };


        ToolboxMessage message = RabbitMqMessageMapper.ToToolboxMessage(
            properties, "{}"u8.ToArray(), "orders.dlx", "orders", redelivered: true);


        message.MessageId.ShouldBe("m-1");
        message.Subject.ShouldBe("OrderPlaced");
        message.TimeToLive.ShouldBe(TimeSpan.FromMinutes(1));
        message.EnqueuedTime.ShouldBe(Now);
        message.RoutingKey.ShouldBe("orders");
        message.SequenceNumber.ShouldBeNull();
        message.DeadLetterSourcePath.ShouldBe("queues/orders");
        message.DeliveryCount.ShouldBe(2);
        message.DeadLetterReason.ShouldBe("rejected");
        message.DeadLetterErrorDescription!.ShouldContain("orders");
        message.ApplicationProperties["tenant"].ShouldBe("contoso");
        message.ApplicationProperties["attempt"].ShouldBe(3);
        message.ApplicationProperties.Keys.ShouldNotContain("x-death");
        message.BrokerProperties["exchange"].ShouldBe("orders.dlx");
        message.BrokerProperties["redelivered"].ShouldBe(true);
        message.BrokerProperties["x-death"].ShouldBe("[{queue=orders, count=1}]");
    }

    [Fact]
    public void ToToolboxMessage_StreamMessage_UsesOffsetAsSequenceNumber()
    {
        BasicProperties properties = new() { Headers = new Dictionary<string, object?> { ["x-stream-offset"] = 41L } };


        ToolboxMessage message = RabbitMqMessageMapper.ToToolboxMessage(properties, "x"u8.ToArray(), string.Empty, "activity", false);


        message.SequenceNumber.ShouldBe(41);
        message.BrokerProperties["exchange"].ShouldBe("(default)");
        message.DeliveryCount.ShouldBe(0);
    }

    [Fact]
    public void ToBasicProperties_Message_IsPersistentTimestampedAndCarriesTypedHeaders()
    {
        ToolboxMessage message = new()
        {
            Body = "{}"u8.ToArray(),
            MessageId = "m-1",
            Subject = "OrderPlaced",
            TimeToLive = TimeSpan.FromSeconds(30),
            ApplicationProperties = new Dictionary<string, object?>
            {
                ["count"] = 5,
                ["id"] = Guid.Empty,
                ["at"] = Now,
            },
        };


        BasicProperties properties = RabbitMqMessageMapper.ToBasicProperties(message, Now);


        properties.DeliveryMode.ShouldBe(DeliveryModes.Persistent);
        properties.Timestamp.UnixTime.ShouldBe(Now.ToUnixTimeSeconds());
        properties.Type.ShouldBe("OrderPlaced");
        properties.Expiration.ShouldBe("30000");
        properties.Headers!["count"].ShouldBe(5);
        properties.Headers["id"].ShouldBe(Guid.Empty.ToString());
        properties.Headers["at"].ShouldBe(new AmqpTimestamp(Now.ToUnixTimeSeconds()));
    }
}
