using MessagingToolbox.Abstractions.Messaging;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqEntityPathTests
{
    [Theory]
    [InlineData("queues/orders", EntityKind.Queue, "orders")]
    [InlineData("exchanges/events", EntityKind.Exchange, "events")]
    [InlineData("queues/a/b", EntityKind.Queue, "a/b")]
    [InlineData("orders", EntityKind.Queue, "orders")]
    public void Parse_Path_ReturnsKindAndBrokerName(string path, EntityKind expectedKind, string expectedName)
    {
        (EntityKind kind, string name) = RabbitMqEntityPath.Parse(path);


        kind.ShouldBe(expectedKind);
        name.ShouldBe(expectedName);
    }

    [Fact]
    public void ForQueueAndForExchange_SameName_ProduceDistinctPaths()
    {
        RabbitMqEntityPath.ForQueue("orders").ShouldNotBe(RabbitMqEntityPath.ForExchange("orders"));
    }
}
