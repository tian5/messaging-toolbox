using System.Globalization;
using System.Text.Json;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// Turns management API rows into display-only <see cref="EntityProperty"/> lists. Queue arguments
/// (<c>x-message-ttl</c>, …) and policy keys (<c>message-ttl</c>, …) share one label table, so a TTL
/// reads the same whether it was declared or applied by policy.
/// </summary>
internal static class RabbitMqEntityProperties
{
    private const string Settings = "Settings";
    private const string Arguments = "Arguments";
    private const string Runtime = "Runtime";
    private const string BoundFrom = "Bound from";
    private const string RoutesTo = "Routes to";

    private enum ValueKind
    {
        Text,
        Milliseconds,
        Bytes,
        Count,
        Exchange,
    }

    private static readonly Dictionary<string, (string Label, ValueKind Kind)> KnownKeys = new(StringComparer.Ordinal)
    {
        ["message-ttl"] = ("Message TTL", ValueKind.Milliseconds),
        ["expires"] = ("Auto-expire when unused", ValueKind.Milliseconds),
        ["max-length"] = ("Max length", ValueKind.Count),
        ["max-length-bytes"] = ("Max length", ValueKind.Bytes),
        ["overflow"] = ("Overflow behaviour", ValueKind.Text),
        ["dead-letter-exchange"] = ("Dead-letter exchange", ValueKind.Exchange),
        ["dead-letter-routing-key"] = ("Dead-letter routing key", ValueKind.Text),
        ["dead-letter-strategy"] = ("Dead-letter strategy", ValueKind.Text),
        ["delivery-limit"] = ("Delivery limit", ValueKind.Count),
        ["max-priority"] = ("Max priority", ValueKind.Count),
        ["queue-mode"] = ("Queue mode", ValueKind.Text),
        ["queue-version"] = ("Queue version", ValueKind.Text),
        ["single-active-consumer"] = ("Single active consumer", ValueKind.Text),
        ["max-age"] = ("Max age", ValueKind.Text),
        ["stream-max-segment-size-bytes"] = ("Segment size", ValueKind.Bytes),
        ["queue-leader-locator"] = ("Leader locator", ValueKind.Text),
        ["initial-cluster-size"] = ("Initial cluster size", ValueKind.Count),
        ["alternate-exchange"] = ("Alternate exchange", ValueKind.Exchange),
    };

    // Bindings come first: where messages come from and go to is what the details are read for.
    // The default exchange's implicit binding (routing key = queue name) is on every queue and says nothing.
    public static IReadOnlyList<EntityProperty> ForQueue(RabbitMqQueueInfo queue, IReadOnlyList<RabbitMqBindingInfo> bindings)
    {
        List<EntityProperty> properties = [];
        AddBindings(properties, BoundFrom, bindings.Where(binding => binding.IsToQueue && binding.Destination == queue.Name && binding.Source.Length > 0),
            binding => binding.Source);
        AddFlag(properties, "Durable", queue.Durable);
        AddFlag(properties, "Auto-delete", queue.AutoDelete);
        AddFlag(properties, "Exclusive", queue.Exclusive);

        // The type is the entity's TypeName already; listing it again as an argument adds nothing.
        AddArguments(properties, Arguments, queue.Arguments, skip: "x-queue-type");
        if (queue.Policy is { Length: > 0 } policy)
        {
            AddArguments(properties, $"Policy {policy}", queue.EffectivePolicyDefinition, skip: null);
        }

        if (queue.Consumers is { } consumers)
        {
            properties.Add(new EntityProperty(Runtime, "Consumers", EntityPropertyFormat.Count(consumers)));
        }

        if (queue.MessagesUnacknowledged is { } unacknowledged)
        {
            properties.Add(new EntityProperty(Runtime, "Unacknowledged", EntityPropertyFormat.Count(unacknowledged))
            {
                Key = "messages_unacknowledged",
            });
        }

        return properties;
    }

    public static IReadOnlyList<EntityProperty> ForExchange(RabbitMqExchangeInfo exchange, IReadOnlyList<RabbitMqBindingInfo> bindings)
    {
        List<EntityProperty> properties = [];
        AddBindings(properties, BoundFrom, bindings.Where(binding => !binding.IsToQueue && binding.Destination == exchange.Name && binding.Source.Length > 0),
            binding => binding.Source);
        AddBindings(properties, RoutesTo, bindings.Where(binding => binding.Source == exchange.Name),
            binding => binding.IsToQueue ? binding.Destination : $"{binding.Destination} (exchange)");
        AddFlag(properties, "Durable", exchange.Durable);
        AddFlag(properties, "Auto-delete", exchange.AutoDelete);
        AddArguments(properties, Arguments, exchange.Arguments, skip: null);
        return properties;
    }

    // One row per binding: the other end as the label, the routing key (and any match arguments) as the value.
    private static void AddBindings(List<EntityProperty> properties, string group, IEnumerable<RabbitMqBindingInfo> bindings, Func<RabbitMqBindingInfo, string> otherEnd)
    {
        foreach (RabbitMqBindingInfo binding in bindings.OrderBy(otherEnd, StringComparer.Ordinal).ThenBy(binding => binding.RoutingKey, StringComparer.Ordinal))
        {
            string value = binding.RoutingKey.Length == 0 ? "(no routing key)" : binding.RoutingKey;
            if (binding.Arguments is { Count: > 0 } arguments)
            {
                value += " · " + string.Join(", ", arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={Format(pair.Value, ValueKind.Text)}"));
            }

            properties.Add(new EntityProperty(group, otherEnd(binding), value) { Key = "routing key" });
        }
    }

    private static void AddFlag(List<EntityProperty> properties, string name, bool? value)
    {
        if (value is { } flag)
        {
            properties.Add(new EntityProperty(Settings, name, EntityPropertyFormat.Flag(flag)));
        }
    }

    private static void AddArguments(List<EntityProperty> properties, string group, Dictionary<string, JsonElement>? values, string? skip)
    {
        if (values is null)
        {
            return;
        }

        foreach ((string key, JsonElement value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (key == skip)
            {
                continue;
            }

            string normalized = key.StartsWith("x-", StringComparison.Ordinal) ? key[2..] : key;
            properties.Add(KnownKeys.TryGetValue(normalized, out (string Label, ValueKind Kind) known)
                ? new EntityProperty(group, known.Label, Format(value, known.Kind)) { Key = key }
                : new EntityProperty(group, key, Format(value, ValueKind.Text)));
        }
    }

    private static string Format(JsonElement value, ValueKind kind)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return kind switch
            {
                ValueKind.Milliseconds => EntityPropertyFormat.Duration(TimeSpan.FromMilliseconds(number)),
                ValueKind.Bytes => EntityPropertyFormat.Bytes(number),
                ValueKind.Count => EntityPropertyFormat.Count(number),
                _ => number.ToString(CultureInfo.InvariantCulture),
            };
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when kind == ValueKind.Exchange && value.GetString() is { Length: 0 } => "(default)",
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => EntityPropertyFormat.Flag(true),
            JsonValueKind.False => EntityPropertyFormat.Flag(false),
            _ => value.GetRawText(),
        };
    }
}
