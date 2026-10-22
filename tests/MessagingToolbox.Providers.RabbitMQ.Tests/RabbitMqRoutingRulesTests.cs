using System.Text.Json;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqRoutingRulesTests
{
    [Fact]
    public void Type_Always_RoutesFromExchangesToQueuesAndExchanges()
    {
        RoutingRuleType type = RabbitMqRoutingRules.Type;


        type.SourceKinds.ShouldBe([EntityKind.Exchange]);
        type.DestinationKinds.ShouldBe([EntityKind.Queue, EntityKind.Exchange]);
        type.Options.Select(option => option.Key).ShouldBe(["routing-key", "arguments"]);
    }

    [Fact]
    public void ToRule_HeadersBindingToExchange_UsesPathsAndFormatsArguments()
    {
        RabbitMqBindingInfo binding = new()
        {
            Source = "events",
            Destination = "archive",
            DestinationType = "exchange",
            PropertiesKey = "~abc",
            Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"x-match":"all","type":"invoice"}"""),
        };


        RoutingRule rule = RabbitMqRoutingRules.ToRule(binding);


        rule.Id.ShouldBe("~abc");
        rule.SourcePath.ShouldBe("exchanges/events");
        rule.DestinationPath.ShouldBe("exchanges/archive");
        rule.Options["routing-key"].ShouldBe(string.Empty);
        rule.Options["arguments"].ShouldBe("type=invoice, x-match=all");
    }

    [Fact]
    public void ToDeclaration_KeyAndArguments_TrimsAndParsesPairs()
    {
        RabbitMqBindingDeclaration declaration = RabbitMqRoutingRules.ToDeclaration(new Dictionary<string, object?>
        {
            ["routing-key"] = " invoice.* ",
            ["arguments"] = "x-match = any, type=invoice",
        });


        declaration.RoutingKey.ShouldBe("invoice.*");
        declaration.Arguments.ShouldBe(new Dictionary<string, object> { ["x-match"] = "any", ["type"] = "invoice" }, ignoreOrder: true);
    }

    [Fact]
    public void ToDeclaration_ArgumentWithoutValue_Throws()
    {
        Dictionary<string, object?> options = new() { ["arguments"] = "x-match" };


        MessagingProviderException exception = Should.Throw<MessagingProviderException>(() => RabbitMqRoutingRules.ToDeclaration(options));


        exception.Message.ShouldContain("key=value");
    }
}
