using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>Body of a management API binding creation (<c>POST /api/bindings/{vhost}/e/{source}/{q|e}/{destination}</c>).</summary>
public sealed record RabbitMqBindingDeclaration(
    [property: JsonPropertyName("routing_key")] string RoutingKey,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object> Arguments);
