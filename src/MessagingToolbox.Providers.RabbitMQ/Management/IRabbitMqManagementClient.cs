namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>
/// The parts of the RabbitMQ management HTTP API the provider uses: discovery and counts. AMQP can't
/// list queues or exchanges, so without this API only config-listed queues are known.
/// </summary>
public interface IRabbitMqManagementClient : IDisposable
{
    /// <summary>Throws when the management API is unreachable or rejects the credentials.</summary>
    Task ProbeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RabbitMqQueueInfo>> ListQueuesAsync(CancellationToken cancellationToken);

    Task<RabbitMqQueueInfo> GetQueueAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<RabbitMqExchangeInfo>> ListExchangesAsync(CancellationToken cancellationToken);
}
