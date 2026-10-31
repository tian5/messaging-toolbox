using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>The management API's error body.</summary>
internal sealed record RabbitMqErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
