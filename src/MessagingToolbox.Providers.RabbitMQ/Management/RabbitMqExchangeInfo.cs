using System.Text.Json;
using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>An exchange as the management API lists it.</summary>
public sealed record RabbitMqExchangeInfo
{
    /// <summary>Empty for the default exchange.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary><c>direct</c>, <c>fanout</c>, <c>topic</c>, <c>headers</c>, or a plugin type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("internal")]
    public bool Internal { get; init; }

    [JsonPropertyName("durable")]
    public bool? Durable { get; init; }

    [JsonPropertyName("auto_delete")]
    public bool? AutoDelete { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement>? Arguments { get; init; }
}
