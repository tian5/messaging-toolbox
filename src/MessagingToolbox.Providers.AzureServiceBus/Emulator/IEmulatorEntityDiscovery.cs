using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus.Emulator;

/// <summary>
/// Strategy for discovering emulator entities without a management plane. Strategies run in
/// order (Aspire model → HTTP admin → SQL backend, architecture doc §6); the first non-null
/// result wins and is merged with AMQP-obtained runtime counts.
/// </summary>
public interface IEmulatorEntityDiscovery
{
    /// <summary>Returns the discovered entities, or null when this strategy is not applicable or unavailable.</summary>
    Task<IReadOnlyList<MessagingEntity>?> DiscoverAsync(CancellationToken cancellationToken);
}
