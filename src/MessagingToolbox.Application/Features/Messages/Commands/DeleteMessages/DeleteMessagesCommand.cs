using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Commands.DeleteMessages;

/// <summary>
/// Permanently deletes the selected messages from any sub-queue. Active and dead-lettered
/// messages must have been received for settlement (lock tokens); scheduled messages are
/// cancelled instead (sequence numbers). Returns the number of messages actually deleted.
/// </summary>
public sealed record DeleteMessagesCommand(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    IReadOnlyList<long> SequenceNumbers,
    IReadOnlyList<string>? LockTokens = null) : IRequest<int>;
