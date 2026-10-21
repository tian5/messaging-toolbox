namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// Settlement of messages identified by broker sequence number on a dead-letter sub-queue,
/// via a bounded peek-lock scan (non-matching messages are abandoned back). See
/// <see cref="IMessagingProvider.SettleBySequenceNumbersAsync"/>.
/// </summary>
public sealed record SettleBySequenceRequest(
    string EntityPath,
    MessageSubQueue SubQueue,
    SettlementOperation Operation,
    IReadOnlyList<long> SequenceNumbers,
    string? Reason,
    string? Description);
