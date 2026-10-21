using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveForSettlementMessages;

/// <summary>
/// Receives messages with peek-lock so the selection can be settled later (by lock token).
/// The messages remain in the queue; locks are held for the entity lock duration, or renewed
/// in the background for <paramref name="LockRenewalSeconds"/> when set. On a session-enabled
/// entity the sessions received from (only <paramref name="SessionId"/> when set) stay locked
/// until their messages are settled.
/// </summary>
public sealed record ReceiveForSettlementMessagesQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    int MaxMessages,
    TimeSpan? MaxWaitTime,
    int? LockRenewalSeconds = null,
    string? SessionId = null) : IRequest<IReadOnlyList<MessageDto>>;
