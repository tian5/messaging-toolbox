namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// The heart of the design: every Blazor feature area and every
/// management command handler checks these flags before acting
/// </summary>
/// <param name="CanManageEntities">Create/update/delete queues, topics, subscriptions.</param>
/// <param name="CanManageRules">Manage subscription filters and actions.</param>
/// <param name="CanPeek">Non-destructive message inspection.</param>
/// <param name="CanReceiveSettle">Peek-lock receive plus complete/abandon/defer/dead-letter.</param>
/// <param name="SupportsSessions">Session-aware entities.</param>
/// <param name="SupportsScheduling">Scheduled enqueue times.</param>
/// <param name="SupportsTransactions">Cross-entity transactional sends/settlements.</param>
/// <param name="MaxMessageSizeBytes">Broker-side message size ceiling (256 KB on the Azure Service Bus emulator, 100 MB on premium namespaces, …).</param>
public sealed record ProviderCapabilities(
    bool CanManageEntities,
    bool CanManageRules,
    bool CanPeek,
    bool CanReceiveSettle,
    bool SupportsSessions,
    bool SupportsScheduling,
    bool SupportsTransactions,
    long MaxMessageSizeBytes)
{
    /// <summary>
    /// Why <see cref="CanManageEntities"/> is off when the provider could normally manage entities
    /// (e.g. the emulator admin endpoint didn't answer); <see langword="null"/> otherwise.
    /// </summary>
    public string? ManagementUnavailableReason { get; init; }
}
