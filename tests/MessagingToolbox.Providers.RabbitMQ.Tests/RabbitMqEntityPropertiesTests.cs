using System.Text.Json;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqEntityPropertiesTests
{
    [Fact]
    public void ForQueue_KnownArguments_UsesLabelsAndFormatsUnits()
    {
        RabbitMqQueueInfo queue = new()
        {
            Name = "orders",
            Type = "quorum",
            Arguments = Json("""{"x-queue-type":"quorum","x-message-ttl":90000,"x-max-length-bytes":1048576,"x-delivery-limit":5,"x-dead-letter-exchange":""}"""),
        };


        IReadOnlyList<EntityProperty> properties = RabbitMqEntityProperties.ForQueue(queue);


        properties.ShouldNotContain(property => property.Key == "x-queue-type");
        properties.ShouldContain(new EntityProperty("Arguments", "Message TTL", "1 min 30 s") { Key = "x-message-ttl" });
        properties.ShouldContain(new EntityProperty("Arguments", "Max length", "1 MB") { Key = "x-max-length-bytes" });
        properties.ShouldContain(new EntityProperty("Arguments", "Delivery limit", "5") { Key = "x-delivery-limit" });
        properties.ShouldContain(new EntityProperty("Arguments", "Dead-letter exchange", "(default)") { Key = "x-dead-letter-exchange" });
    }

    [Fact]
    public void ForQueue_UnknownArgument_KeepsRawKeyAndValue()
    {
        RabbitMqQueueInfo queue = new() { Name = "orders", Arguments = Json("""{"x-custom":{"a":1}}""") };


        IReadOnlyList<EntityProperty> properties = RabbitMqEntityProperties.ForQueue(queue);


        properties.ShouldHaveSingleItem().ShouldBe(new EntityProperty("Arguments", "x-custom", """{"a":1}"""));
    }

    [Fact]
    public void ForQueue_SettingsPolicyAndRuntime_GroupsThem()
    {
        RabbitMqQueueInfo queue = new()
        {
            Name = "orders",
            Durable = true,
            AutoDelete = false,
            Consumers = 2,
            MessagesUnacknowledged = 1,
            Policy = "ttl-policy",
            EffectivePolicyDefinition = Json("""{"message-ttl":5000}"""),
        };


        IReadOnlyList<EntityProperty> properties = RabbitMqEntityProperties.ForQueue(queue);


        properties.ShouldContain(new EntityProperty("Settings", "Durable", "yes"));
        properties.ShouldContain(new EntityProperty("Settings", "Auto-delete", "no"));
        properties.ShouldContain(new EntityProperty("Policy ttl-policy", "Message TTL", "5 s") { Key = "message-ttl" });
        properties.ShouldContain(new EntityProperty("Runtime", "Consumers", "2"));
        properties.ShouldContain(property => property.Group == "Runtime" && property.Name == "Unacknowledged" && property.Value == "1");
        properties.ShouldNotContain(property => property.Name == "Exclusive");
    }

    [Fact]
    public void ForExchange_AlternateExchange_IsLabelled()
    {
        RabbitMqExchangeInfo exchange = new() { Name = "events", Durable = true, Arguments = Json("""{"alternate-exchange":"unrouted"}""") };


        IReadOnlyList<EntityProperty> properties = RabbitMqEntityProperties.ForExchange(exchange);


        properties.ShouldBe(
        [
            new EntityProperty("Settings", "Durable", "yes"),
            new EntityProperty("Arguments", "Alternate exchange", "unrouted") { Key = "alternate-exchange" },
        ]);
    }

    private static Dictionary<string, JsonElement> Json(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
