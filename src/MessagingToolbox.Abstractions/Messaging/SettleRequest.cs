namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Settles messages previously received via a settleable (peek-lock) receive, by lock token.</summary>
/// <param name="EntityPath">Queue name or "{topic}/subscriptions/{subscription}".</param>
/// <param name="SubQueue">Which sub-queue the messages were received from.</param>
/// <param name="Operation">Complete / abandon / defer / dead-letter.</param>
/// <param name="LockTokens">Lock tokens of the messages to settle; each must currently be held.</param>
/// <param name="Reason">Dead-letter reason (dead-letter operation only).</param>
/// <param name="Description">Dead-letter description (dead-letter operation only).</param>
public sealed record SettleRequest(
    string EntityPath,
    MessageSubQueue SubQueue,
    SettlementOperation Operation,
    IReadOnlyList<string> LockTokens,
    string? Reason,
    string? Description);
