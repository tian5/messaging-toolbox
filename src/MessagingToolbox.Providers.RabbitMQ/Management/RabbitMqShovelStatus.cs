using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>A running shovel from <c>/api/shovels/{vhost}</c> (shovel management plugin).</summary>
public sealed record RabbitMqShovelStatus
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary><c>starting</c>, <c>running</c> or <c>terminated</c>.</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    /// <summary>Why a terminated shovel stopped.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonIgnore]
    public bool IsTerminated => State == "terminated";
}
