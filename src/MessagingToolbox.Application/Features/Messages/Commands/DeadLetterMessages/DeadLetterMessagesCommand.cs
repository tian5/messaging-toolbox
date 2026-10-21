using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;

/// <summary>
/// Moves the selected messages to the dead-letter queue. Active messages must have been
/// received for settlement (lock tokens); scheduled messages are cancelled and re-sent for
/// immediate delivery before being dead-lettered (sequence numbers). Only active and scheduled
/// messages qualify — a dead-lettered message cannot be dead-lettered again. Returns the number
/// of messages actually moved.
/// </summary>
public sealed record DeadLetterMessagesCommand(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    IReadOnlyList<long> SequenceNumbers,
    IReadOnlyList<string>? LockTokens = null,
    string? Reason = null,
    string? Description = null) : IRequest<int>;
