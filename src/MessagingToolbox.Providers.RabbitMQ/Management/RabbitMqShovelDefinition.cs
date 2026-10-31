using System.Text.Json.Serialization;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>
/// Value of a dynamic shovel parameter (<c>PUT /api/parameters/shovel/{vhost}/{name}</c>) that moves
/// a queue's messages to another queue of the same vhost.
/// </summary>
public sealed record RabbitMqShovelDefinition
{
    [JsonPropertyName("src-protocol")]
    public string SourceProtocol { get; init; } = "amqp091";

    [JsonPropertyName("src-uri")]
    public required string SourceUri { get; init; }

    [JsonPropertyName("src-queue")]
    public required string SourceQueue { get; init; }

    // Never declare: a move must not create (or fail on re-declaring) either queue.
    [JsonPropertyName("src-predeclared")]
    public bool SourcePredeclared { get; init; } = true;

    // Move what is there when the shovel starts, then delete the shovel.
    [JsonPropertyName("src-delete-after")]
    public string SourceDeleteAfter { get; init; } = "queue-length";

    [JsonPropertyName("dest-protocol")]
    public string DestinationProtocol { get; init; } = "amqp091";

    [JsonPropertyName("dest-uri")]
    public required string DestinationUri { get; init; }

    [JsonPropertyName("dest-queue")]
    public required string DestinationQueue { get; init; }

    [JsonPropertyName("dest-predeclared")]
    public bool DestinationPredeclared { get; init; } = true;

    // A message leaves the source only once the destination confirmed it.
    [JsonPropertyName("ack-mode")]
    public string AckMode { get; init; } = "on-confirm";

    /// <summary>A move between two queues of <paramref name="virtualHost"/>, over the broker's direct (in-node) connection.</summary>
    public static RabbitMqShovelDefinition Move(string virtualHost, string sourceQueue, string destinationQueue)
    {
        string uri = $"amqp:///{Uri.EscapeDataString(virtualHost)}";
        return new RabbitMqShovelDefinition
        {
            SourceUri = uri,
            SourceQueue = sourceQueue,
            DestinationUri = uri,
            DestinationQueue = destinationQueue,
        };
    }
}
