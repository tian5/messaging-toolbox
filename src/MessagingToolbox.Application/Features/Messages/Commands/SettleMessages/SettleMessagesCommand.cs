using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;

/// <summary>Settles messages received for settlement: complete / abandon / defer / dead-letter.</summary>
public sealed record SettleMessagesCommand(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    SettlementOperation Operation,
    IReadOnlyList<string> LockTokens,
    string? Reason,
    string? Description) : IRequest;
