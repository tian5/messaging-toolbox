using System.Globalization;
using System.Text.Json;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// RabbitMQ bindings as routing rules (ADR 22): from an exchange to a queue or another exchange,
/// matched by routing key or, on headers exchanges, by arguments. The default exchange's implicit
/// binding to every queue can't be changed and is left out.
/// </summary>
internal static class RabbitMqRoutingRules
{
    public const string RoutingKey = "routing-key";
    public const string Arguments = "arguments";

    public static readonly RoutingRuleType Type = new(
        "binding",
        [EntityKind.Exchange],
        [EntityKind.Queue, EntityKind.Exchange],
        [
            new EntityOptionDescriptor(RoutingKey, "Routing key", EntityOptionType.Text)
            {
                Placeholder = "orders.* or #",
                Description = "Direct exchanges match it exactly, topic exchanges as a pattern (* one word, # any); fanout and headers exchanges ignore it.",
            },
            new EntityOptionDescriptor(Arguments, "Arguments", EntityOptionType.Text)
            {
                Placeholder = "x-match=all, type=invoice",
                Description = "Headers exchanges match on these instead of the routing key; key=value pairs, comma-separated.",
            },
        ])
    {
        Description = "A binding routes messages from an exchange to a queue or another exchange.",
    };

    public static RoutingRule ToRule(RabbitMqBindingInfo binding) => new(
        binding.PropertiesKey,
        RabbitMqEntityPath.ForExchange(binding.Source),
        binding.IsToQueue ? RabbitMqEntityPath.ForQueue(binding.Destination) : RabbitMqEntityPath.ForExchange(binding.Destination),
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [RoutingKey] = binding.RoutingKey,
            [Arguments] = FormatArguments(binding.Arguments),
        });

    public static RabbitMqBindingDeclaration ToDeclaration(IReadOnlyDictionary<string, object?> options) => new(
        (options.GetValueOrDefault(RoutingKey) as string)?.Trim() ?? string.Empty,
        ParseArguments(options.GetValueOrDefault(Arguments) as string));

    // "x-match=all, type=invoice" → {x-match: all, type: invoice}. Values are strings, which is what
    // headers-exchange matching compares against string headers.
    private static Dictionary<string, object> ParseArguments(string? text)
    {
        Dictionary<string, object> arguments = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return arguments;
        }

        foreach (string pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new MessagingProviderException($"Binding arguments are key=value pairs separated by commas; '{pair}' isn't one.");
            }

            arguments[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }

        return arguments;
    }

    private static string FormatArguments(Dictionary<string, JsonElement>? arguments) => arguments is null
        ? string.Empty
        : string.Join(", ", arguments
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={(pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() : pair.Value.GetRawText())}"));
}
