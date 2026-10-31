using System.Collections.Concurrent;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Infrastructure.Messaging;

/// <summary>
/// Singleton cache of providers and their broker clients, keyed by connection (architecture doc §5:
/// clients are expensive, Mediator handlers stay scoped). Creation is dispatched to the
/// <see cref="IMessagingProviderBuilder"/> registered for the connection's kind.
/// </summary>
public sealed class MessagingProviderFactory : IMessagingProviderFactory, IAsyncDisposable
{
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly ILogger<MessagingProviderFactory> _logger;
    private readonly IReadOnlyDictionary<ProviderKind, IMessagingProviderBuilder> _builders;
    private readonly ConcurrentDictionary<Guid, Lazy<Task<IMessagingProvider>>> _providers = new();

    public MessagingProviderFactory(
        IConnectionCatalog connectionCatalog,
        IEnumerable<IMessagingProviderBuilder> builders,
        ILogger<MessagingProviderFactory> logger)
    {
        _connectionCatalog = connectionCatalog;
        _logger = logger;
        _builders = builders.ToDictionary(builder => builder.Kind);
    }

    public async Task<IMessagingProvider> GetProviderAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken)
            ?? throw new MessagingProviderException($"Unknown connection '{connectionId}'.");

        if (!_builders.TryGetValue(connection.Kind, out IMessagingProviderBuilder? builder))
        {
            throw new NotSupportedByProviderException(
                $"Provider kind '{connection.Kind}' is not available in this toolbox build.");
        }

        return await GetOrAddProviderAsync(connection, builder, cancellationToken);
    }

    public async Task DisposeAsync(Guid connectionId)
    {
        if (_providers.TryRemove(connectionId, out Lazy<Task<IMessagingProvider>>? entryTask))
        {
            await DisposeEntryAsync(entryTask);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Lazy<Task<IMessagingProvider>> entryTask in _providers.Values)
        {
            await DisposeEntryAsync(entryTask);
        }

        _providers.Clear();
    }

    // Eviction runs on profile save/delete: a creation that failed earlier has nothing to dispose
    // and must not resurface its old error as a failure of the save; one entry failing to dispose
    // must not keep the others alive.
    private async Task DisposeEntryAsync(Lazy<Task<IMessagingProvider>> entryTask)
    {
        if (!entryTask.IsValueCreated)
        {
            return;
        }

        try
        {
            IMessagingProvider provider = await entryTask.Value;
            await provider.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Evicting a cached provider failed or its creation had failed; nothing left to dispose.");
        }
    }

    private async Task<IMessagingProvider> GetOrAddProviderAsync(
        ConnectionInfo connection, IMessagingProviderBuilder builder, CancellationToken cancellationToken)
    {
        // The cached creation task must not capture the caller's token: a cancelled first
        // creation would poison the cache and rethrow for every later caller until restart.
        Lazy<Task<IMessagingProvider>> lazy = _providers.GetOrAdd(
            connection.Id,
            _ => new Lazy<Task<IMessagingProvider>>(
                () => CreateAsync(builder, connection),
                LazyThreadSafetyMode.ExecutionAndPublication));

        Task<IMessagingProvider> entry = lazy.Value;

        try
        {
            return await entry.WaitAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A failed creation is evicted so the next call retries cleanly. A caller-side
            // cancellation leaves the in-flight creation cached — it may still succeed and
            // serve the next caller.
            if (entry.IsFaulted || entry.IsCanceled)
            {
                _providers.TryRemove(new KeyValuePair<Guid, Lazy<Task<IMessagingProvider>>>(connection.Id, lazy));
            }

            throw;
        }
    }

    // Async so a builder that throws synchronously still yields a faulted task (which is evicted
    // above) instead of an exception the Lazy would cache for good.
    private static async Task<IMessagingProvider> CreateAsync(IMessagingProviderBuilder builder, ConnectionInfo connection) =>
        await builder.CreateAsync(connection, CancellationToken.None);
}
