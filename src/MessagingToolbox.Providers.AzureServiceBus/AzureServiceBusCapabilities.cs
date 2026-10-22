using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>
/// Capability profiles for the two Azure Service Bus modes. The emulator profile is what makes
/// the UI hide management actions — driven by capabilities, never by provider-type checks (ADR 5).
/// </summary>
public static class AzureServiceBusCapabilities
{
    /// <summary>
    /// Cloud namespace: full data and management plane. The size ceiling uses the premium-tier
    /// maximum; the broker stays authoritative on smaller tiers.
    /// </summary>
    public static readonly ProviderCapabilities Cloud = new()
    {
        CanManageEntities = true,
        CanManageRules = true,
        CanPeek = true,
        CanReceiveSettle = true,
        SupportsSessions = true,
        SupportsScheduling = true,
        SupportsTransactions = true,
        SupportsDeadLetterSubQueue = true,
        SupportsDeferral = true,
        SupportsSequenceNumbers = true,
        SupportsLockExpiry = true,
        MaxMessageSizeBytes = 100 * 1024 * 1024,
    };

    /// <summary>
    /// Azure Service Bus emulator: data plane plus a probed management plane. The ≥ 2.0.0 emulator
    /// exposes <see cref="ServiceBusAdministrationClient"/> on port 5300; when the probe succeeds,
    /// <see cref="ProviderCapabilities.CanManageEntities"/> is flipped to <see langword="true"/>.
    /// No transactions; max 256 KB messages.
    /// </summary>
    public static readonly ProviderCapabilities Emulator = new()
    {
        CanManageEntities = false,
        CanManageRules = false,
        CanPeek = true,
        CanReceiveSettle = true,
        SupportsSessions = true,
        SupportsScheduling = true,
        SupportsTransactions = false,
        SupportsDeadLetterSubQueue = true,
        SupportsDeferral = true,
        SupportsSequenceNumbers = true,
        SupportsLockExpiry = true,
        MaxMessageSizeBytes = 256 * 1024,
    };
}
