namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>
/// The parts of the RabbitMQ management HTTP API the provider uses: discovery, counts and entity
/// management. AMQP can't list queues or exchanges, so without this API only config-listed queues
/// are known. Failures surface as <see cref="HttpRequestException"/> carrying the status code and
/// the broker's reason as the message.
/// </summary>
public interface IRabbitMqManagementClient : IDisposable
{
    /// <summary>Throws when the management API is unreachable or rejects the credentials.</summary>
    Task ProbeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RabbitMqQueueInfo>> ListQueuesAsync(CancellationToken cancellationToken);

    Task<RabbitMqQueueInfo> GetQueueAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<RabbitMqExchangeInfo>> ListExchangesAsync(CancellationToken cancellationToken);

    Task<RabbitMqExchangeInfo> GetExchangeAsync(string name, CancellationToken cancellationToken);

    /// <summary>Every binding in the vhost, including the default exchange's implicit ones.</summary>
    Task<IReadOnlyList<RabbitMqBindingInfo>> ListBindingsAsync(CancellationToken cancellationToken);

    /// <summary>Binds <paramref name="destination"/> (a queue, or an exchange) to the source exchange; returns the binding's properties key.</summary>
    Task<string> CreateBindingAsync(
        string source,
        string destination,
        bool destinationIsQueue,
        RabbitMqBindingDeclaration declaration,
        CancellationToken cancellationToken);

    Task DeleteBindingAsync(string source, string destination, bool destinationIsQueue, string propertiesKey, CancellationToken cancellationToken);

    /// <summary>Declares the queue; a no-op when it exists with the same settings, a 400 when they differ.</summary>
    Task DeclareQueueAsync(string name, RabbitMqQueueDeclaration declaration, CancellationToken cancellationToken);

    Task DeleteQueueAsync(string name, CancellationToken cancellationToken);

    /// <summary>Declares the exchange; a no-op when it exists with the same settings, a 400 when they differ.</summary>
    Task DeclareExchangeAsync(string name, RabbitMqExchangeDeclaration declaration, CancellationToken cancellationToken);

    Task DeleteExchangeAsync(string name, CancellationToken cancellationToken);
}
