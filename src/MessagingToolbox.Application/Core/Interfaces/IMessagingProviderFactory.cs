using MessagingToolbox.Abstractions;

namespace MessagingToolbox.Application.Core.Interfaces;

/// <summary>
/// Resolves (and caches) the provider and its underlying clients for a connection profile.
/// Broker clients are expensive — implementations should keep a singleton cache while Mediator
/// handlers stay scoped.
/// </summary>
public interface IMessagingProviderFactory
{
    Task<IMessagingProvider> GetProviderAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>Disposes cached clients when a profile changes or is removed.</summary>
    Task DisposeAsync(Guid connectionId);
}
