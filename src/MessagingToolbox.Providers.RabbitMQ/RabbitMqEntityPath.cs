using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// Entity paths of a RabbitMQ connection: <c>queues/{name}</c> and <c>exchanges/{name}</c>. A queue
/// and an exchange may share a name, so the kind is part of the path; one connection is one vhost.
/// </summary>
internal static class RabbitMqEntityPath
{
    private const string QueuePrefix = "queues/";
    private const string ExchangePrefix = "exchanges/";

    public static string ForQueue(string name) => QueuePrefix + name;

    public static string ForExchange(string name) => ExchangePrefix + name;

    /// <summary>Splits a path into kind and broker name; a bare name is taken as a queue (config-listed queues).</summary>
    public static (EntityKind Kind, string Name) Parse(string path)
    {
        if (path.StartsWith(ExchangePrefix, StringComparison.Ordinal))
        {
            return (EntityKind.Exchange, path[ExchangePrefix.Length..]);
        }

        return path.StartsWith(QueuePrefix, StringComparison.Ordinal)
            ? (EntityKind.Queue, path[QueuePrefix.Length..])
            : (EntityKind.Queue, path);
    }
}
