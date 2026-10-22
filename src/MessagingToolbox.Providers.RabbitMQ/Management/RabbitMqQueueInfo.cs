using System.Text.Json;
using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>A queue as the management API lists it (only the columns the toolbox requests).</summary>
public sealed record RabbitMqQueueInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary><c>classic</c>, <c>quorum</c> or <c>stream</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Ready plus unacknowledged; null until the broker has collected stats.</summary>
    [JsonPropertyName("messages")]
    public long? Messages { get; init; }

    [JsonPropertyName("messages_ready")]
    public long? MessagesReady { get; init; }

    [JsonPropertyName("messages_unacknowledged")]
    public long? MessagesUnacknowledged { get; init; }

    [JsonPropertyName("message_bytes")]
    public long? MessageBytes { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement>? Arguments { get; init; }

    public bool IsStream => string.Equals(Type, "stream", StringComparison.Ordinal);
}
