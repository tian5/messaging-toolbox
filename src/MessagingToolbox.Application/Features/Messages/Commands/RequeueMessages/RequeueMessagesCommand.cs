using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;

/// <summary>
/// Requeues dead-lettered messages into a user-chosen queue or topic as exact copies
/// (clone semantics, FR-4.6). The originals stay in the dead-letter queue unless
/// <paramref name="RemoveOriginals"/> is set, in which case they are completed via their
/// held lock tokens after the copies landed. Returns the number of copies sent.
/// </summary>
public sealed record RequeueMessagesCommand(
    Guid ConnectionId,
    string SourceEntityPath,
    MessageSubQueue SourceSubQueue,
    string TargetPath,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<string>? LockTokens = null,
    bool RemoveOriginals = false) : IRequest<int>;
