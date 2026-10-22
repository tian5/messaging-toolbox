namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// What one entity allows on top of the connection's <see cref="ProviderCapabilities"/>, for
/// brokers whose entities differ within one connection (a RabbitMQ stream can't be consumed away
/// or purged). It only narrows: an operation is offered when the capabilities <em>and</em> this
/// allow it. Defaults allow everything, and the provider still refuses what it can't do (ADR 20).
/// </summary>
public sealed record EntityOperations
{
    /// <summary>Nothing narrowed: the connection's capabilities decide alone.</summary>
    public static EntityOperations All { get; } = new();

    /// <summary>Receive-and-delete.</summary>
    public bool CanReceive { get; init; } = true;

    /// <summary>Settleable receive (and with it complete/abandon/dead-letter of held messages).</summary>
    public bool CanReceiveSettle { get; init; } = true;

    /// <summary>Remove every message at once.</summary>
    public bool CanPurge { get; init; } = true;

    /// <summary>Why some operations are off, shown as a hint; null when nothing is.</summary>
    public string? Reason { get; init; }
}
