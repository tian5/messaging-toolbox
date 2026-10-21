using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus.Emulator;

/// <summary>
/// Supplies runtime counts for emulator connections. The emulator exposes no management plane
/// (its HTTP feed serves the configured entities but never live counts), so counts are obtained
/// over AMQP instead (architecture doc §6: discovery results "merged with AMQP-obtained runtime
/// counts"). Sampled counts can be approximate at the sampling cap.
/// </summary>
public interface IEmulatorRuntimeCountSampler
{
    /// <summary>Samples the counts of one queue or subscription path. Throws when the broker is unreachable.</summary>
    Task<EntityRuntimeInfo> SampleAsync(string entityPath, CancellationToken cancellationToken);

    /// <summary>
    /// Drops any cached counts for <paramref name="entityPath"/> (and, for a topic, for all of its
    /// subscriptions). Providers call this after a mutating operation so the next sample reflects
    /// the mutation instead of a stale cache entry — including a walk already in flight.
    /// </summary>
    void Invalidate(string entityPath);
}
