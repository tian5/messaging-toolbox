using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>Body of a management API exchange declaration (<c>PUT /api/exchanges/{vhost}/{name}</c>).</summary>
public sealed record RabbitMqExchangeDeclaration(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("durable")] bool Durable,
    [property: JsonPropertyName("auto_delete")] bool AutoDelete,
    [property: JsonPropertyName("internal")] bool Internal,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object> Arguments);
