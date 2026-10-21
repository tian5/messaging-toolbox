using System.Collections.Concurrent;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Providers.AzureServiceBus.Emulator;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// Singleton cache of providers and their AMQP clients, keyed by connection (architecture doc §5:
/// clients are expensive, Mediator handlers stay scoped). New provider kinds dispatch here —
/// a future Providers.RabbitMQ plugs into the same factory seam.
/// </summary>
public sealed class MessagingProviderFactory : IMessagingProviderFactory, IAsyncDisposable
{
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IAzureServiceBusManagementFactory _managementFactory;
    private readonly ConcurrentDictionary<Guid, Lazy<Task<ProviderCacheEntry>>> _providers = new();

    public MessagingProviderFactory(
        IConnectionCatalog connectionCatalog,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IAzureServiceBusManagementFactory managementFactory)
    {
        _connectionCatalog = connectionCatalog;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _managementFactory = managementFactory;
    }

    public async Task<IMessagingProvider> GetProviderAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken)
            ?? throw new MessagingProviderException($"Unknown connection '{connectionId}'.");

        return connection.Kind switch
        {
            ProviderKind.AzureServiceBus => await GetOrAddAzureServiceBusProviderAsync(connection, cancellationToken),
            _ => throw new NotSupportedByProviderException(
                $"Provider kind '{connection.Kind}' is not implemented yet — RabbitMQ, Kafka, and NATS are on the roadmap."),
        };
    }

    public async Task DisposeAsync(Guid connectionId)
    {
        if (_providers.TryRemove(connectionId, out Lazy<Task<ProviderCacheEntry>>? entryTask))
        {
            await DisposeEntryAsync(entryTask);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Lazy<Task<ProviderCacheEntry>> entryTask in _providers.Values)
        {
            await DisposeEntryAsync(entryTask);
        }

        _providers.Clear();
    }

    // Eviction runs on profile save/delete: a creation that failed earlier has nothing to dispose
    // and must not resurface its old error as a failure of the save; one entry failing to dispose
    // must not keep the others alive.
    private async Task DisposeEntryAsync(Lazy<Task<ProviderCacheEntry>> entryTask)
    {
        if (!entryTask.IsValueCreated)
        {
            return;
        }

        try
        {
            ProviderCacheEntry entry = await entryTask.Value;
            await entry.DisposeAsync();
        }
        catch (Exception exception)
        {
            _loggerFactory.CreateLogger<MessagingProviderFactory>()
                .LogDebug(exception, "Evicting a cached provider failed or its creation had failed; nothing left to dispose.");
        }
    }

    private async Task<IMessagingProvider> GetOrAddAzureServiceBusProviderAsync(
        ConnectionInfo connection, CancellationToken cancellationToken)
    {
        // The cached creation task must not capture the caller's token: a cancelled first
        // creation would poison the cache and rethrow for every later caller until restart.
        Lazy<Task<ProviderCacheEntry>> lazy = _providers.GetOrAdd(
            connection.Id,
            _ => new Lazy<Task<ProviderCacheEntry>>(
                () => CreateAzureServiceBusProviderAsync(connection, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        Task<ProviderCacheEntry> entry = lazy.Value;

        try
        {
            return (await entry.WaitAsync(cancellationToken)).Provider;
        }
        catch (Exception)
        {
            // A failed creation is evicted so the next call retries cleanly. A caller-side
            // cancellation leaves the in-flight creation cached — it may still succeed and
            // serve the next caller.
            if (entry.IsFaulted || entry.IsCanceled)
            {
                _providers.TryRemove(new KeyValuePair<Guid, Lazy<Task<ProviderCacheEntry>>>(connection.Id, lazy));
            }

            throw;
        }
    }

    private async Task<ProviderCacheEntry> CreateAzureServiceBusProviderAsync(
        ConnectionInfo connection, CancellationToken cancellationToken)
    {
        AzureServiceBusProvider provider = await AzureServiceBusProvider.CreateAsync(
            connection.ConnectionString,
            [new AspireModelDiscovery(connection.ModeledQueues, connection.ModeledTopics)],
            _loggerFactory.CreateLogger<AzureServiceBusProvider>(),
            _timeProvider,
            _managementFactory.Create(connection.ConnectionString, connection.AdminEndpoint),
            cancellationToken);

        return new ProviderCacheEntry(provider);
    }

    private sealed record ProviderCacheEntry(AzureServiceBusProvider Provider) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
    }
}
