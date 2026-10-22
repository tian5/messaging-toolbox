using System.Text.Json;
using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>A binding as the management API lists it: messages flow from the source exchange to the destination.</summary>
public sealed record RabbitMqBindingInfo
{
    /// <summary>Source exchange; empty for the default exchange's implicit binding to every queue.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("destination")]
    public required string Destination { get; init; }

    /// <summary><c>queue</c> or <c>exchange</c>.</summary>
    [JsonPropertyName("destination_type")]
    public required string DestinationType { get; init; }

    [JsonPropertyName("routing_key")]
    public string RoutingKey { get; init; } = string.Empty;

    /// <summary>Match arguments (headers exchanges: <c>x-match</c> and the headers to match).</summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement>? Arguments { get; init; }

    public bool IsToQueue => string.Equals(DestinationType, "queue", StringComparison.Ordinal);
}
