using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Tests.Common;

/// <summary>Capability profiles for handler tests; vary single flags with <c>with { … }</c>.</summary>
internal static class TestCapabilities
{
    /// <summary>Everything supported (an Azure Service Bus cloud namespace, emulator-sized messages).</summary>
    public static readonly ProviderCapabilities Full = new()
    {
        CanManageEntities = true,
        CanManageRules = true,
        CanPeek = true,
        CanReceiveSettle = true,
        SupportsSessions = true,
        SupportsScheduling = true,
        SupportsTransactions = true,
        SupportsDeadLetterSubQueue = true,
        SupportsDeadLetterReason = true,
        SupportsDeferral = true,
        SupportsSequenceNumbers = true,
        SupportsLockExpiry = true,
        MaxMessageSizeBytes = 256 * 1024,
    };

    /// <summary>Data plane only, no transactions (the Azure Service Bus emulator before its admin probe).</summary>
    public static readonly ProviderCapabilities DataPlane = Full with
    {
        CanManageEntities = false,
        CanManageRules = false,
        SupportsTransactions = false,
    };
}
