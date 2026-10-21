using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.SettleBySequence;

/// <summary>
/// Settles messages on a dead-letter sub-queue by broker sequence number (bounded scan-and-settle
/// at the provider). The fallback for DLQ views loaded with peek, where no lock tokens are held.
/// Returns the number of messages actually settled.
/// </summary>
public sealed record SettleBySequenceCommand(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    SettlementOperation Operation,
    IReadOnlyList<long> SequenceNumbers,
    string? Reason = null,
    string? Description = null) : IRequest<int>;
