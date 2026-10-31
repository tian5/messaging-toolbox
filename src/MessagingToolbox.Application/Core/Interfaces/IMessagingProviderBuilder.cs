using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;

namespace MessagingToolbox.Application.Core.Interfaces;

/// <summary>
/// Creates the provider for one broker kind. Each provider project registers its builder; the
/// shared <see cref="IMessagingProviderFactory"/> dispatches to it by <see cref="Kind"/> and owns
/// caching and disposal, so adding a broker never touches the factory.
/// </summary>
public interface IMessagingProviderBuilder
{
    ProviderKind Kind { get; }

    /// <summary>Creates (and probes) a provider for <paramref name="connection"/>.</summary>
    Task<IMessagingProvider> CreateAsync(ConnectionInfo connection, CancellationToken cancellationToken);
}
