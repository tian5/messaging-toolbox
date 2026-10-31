using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>Body of a management API queue declaration (<c>PUT /api/queues/{vhost}/{name}</c>).</summary>
public sealed record RabbitMqQueueDeclaration(
    [property: JsonPropertyName("durable")] bool Durable,
    [property: JsonPropertyName("auto_delete")] bool AutoDelete,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object> Arguments);
