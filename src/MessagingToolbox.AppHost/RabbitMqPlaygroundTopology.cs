using System.Text;
using RabbitMQ.Client;

namespace MessagingToolbox.AppHost;

/// <summary>
/// Playground topology for the RabbitMQ broker. Aspire doesn't model queues or exchanges, so the
/// AppHost declares them once the broker is ready (idempotent: declares are no-ops when they exist).
/// Covers what the toolbox distinguishes: a quorum queue dead-lettering into a classic one, a topic
/// exchange fanning out by routing key, and a stream.
/// </summary>
internal static class RabbitMqPlaygroundTopology
{
    public static async Task SeedAsync(string connectionString, CancellationToken cancellationToken)
    {
        ConnectionFactory factory = new() { Uri = new Uri(connectionString), ClientProvidedName = "MessagingToolbox.AppHost" };
        await using IConnection connection = await factory.CreateConnectionAsync(cancellationToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync("orders.dlx", ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync("orders.dlq", durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync("orders.dlq", "orders.dlx", routingKey: string.Empty, cancellationToken: cancellationToken);

        QueueDeclareOk orders = await channel.QueueDeclareAsync(
            "orders",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = "orders.dlx",
            },
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync("events", ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync("billing", durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync("audit", durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync("billing", "events", routingKey: "billing.#", cancellationToken: cancellationToken);
        await channel.QueueBindAsync("audit", "events", routingKey: "#", cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            "activity",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "stream" },
            cancellationToken: cancellationToken);

        // A few messages to browse right away, only into a fresh broker.
        if (orders.MessageCount == 0)
        {
            for (int index = 1; index <= 3; index++)
            {
                BasicProperties properties = new() { ContentType = "application/json", MessageId = Guid.NewGuid().ToString() };
                byte[] body = Encoding.UTF8.GetBytes($$"""{"orderId":{{index}},"total":{{index * 10}}.5}""");
                await channel.BasicPublishAsync(string.Empty, "orders", mandatory: false, properties, body, cancellationToken);
            }
        }
    }
}
