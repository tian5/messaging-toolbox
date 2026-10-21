using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;

/// <summary>
/// Receives deferred messages by sequence number with peek-lock so they can be settled (by lock
/// token) — the only way to get a deferred message back. Messages that are no longer deferred, are
/// locked elsewhere or are gone are missing from the result. Locks are renewed in the background
/// for <paramref name="LockRenewalSeconds"/> when set.
/// </summary>
public sealed record ReceiveDeferredMessagesQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    IReadOnlyList<DeferredMessageReference> Messages,
    int? LockRenewalSeconds = null) : IRequest<IReadOnlyList<MessageDto>>;
