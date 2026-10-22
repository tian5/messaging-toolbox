using System.Text.Json;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqEntityTypesTests
{
    [Fact]
    public void All_Always_DescribesCreateOnlyQueuesAndExchanges()
    {
        IReadOnlyList<EntityTypeDescriptor> types = RabbitMqEntityTypes.All;


        types.Select(type => type.Kind).ShouldBe([EntityKind.Queue, EntityKind.Exchange]);
        types.SelectMany(type => type.Options).ShouldAllBe(option => option.CreateOnly);
    }

    [Fact]
    public void ToQueueDeclaration_Options_SplitsFieldsFromArgumentsAndConvertsDurations()
    {
        Dictionary<string, object?> options = new()
        {
            ["x-queue-type"] = "quorum",
            ["durable"] = true,
            ["auto-delete"] = false,
            ["x-message-ttl"] = TimeSpan.FromSeconds(90),
            ["x-delivery-limit"] = 5L,
            ["x-dead-letter-exchange"] = " orders.dlx ",
            ["x-dead-letter-routing-key"] = "",
            ["x-overflow"] = null,
        };


        RabbitMqQueueDeclaration declaration = RabbitMqEntityTypes.ToQueueDeclaration(options);


        declaration.Durable.ShouldBeTrue();
        declaration.AutoDelete.ShouldBeFalse();
        declaration.Arguments.ShouldBe(new Dictionary<string, object>
        {
            ["x-queue-type"] = "quorum",
            ["x-message-ttl"] = 90_000L,
            ["x-delivery-limit"] = 5L,
            ["x-dead-letter-exchange"] = "orders.dlx",
        }, ignoreOrder: true);
    }

    [Fact]
    public void ToExchangeDeclaration_NoType_DefaultsToDurableDirect()
    {
        RabbitMqExchangeDeclaration declaration = RabbitMqEntityTypes.ToExchangeDeclaration(new Dictionary<string, object?>());


        declaration.ShouldBe(new RabbitMqExchangeDeclaration("direct", true, false, false, declaration.Arguments));
        declaration.Arguments.ShouldBeEmpty();
    }

    [Fact]
    public void FromQueue_DeclaredQueue_ReadsTypeFieldsAndTypedArguments()
    {
        RabbitMqQueueInfo queue = new()
        {
            Name = "orders",
            Type = "quorum",
            Durable = true,
            AutoDelete = false,
            Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"x-queue-type":"quorum","x-message-ttl":5000,"x-max-length":10,"x-dead-letter-exchange":"dlx"}"""),
        };


        IReadOnlyDictionary<string, object?> options = RabbitMqEntityTypes.FromQueue(queue);


        options["x-queue-type"].ShouldBe("quorum");
        options["durable"].ShouldBe(true);
        options["x-message-ttl"].ShouldBe(TimeSpan.FromSeconds(5));
        options["x-max-length"].ShouldBe(10L);
        options["x-dead-letter-exchange"].ShouldBe("dlx");
    }
}
