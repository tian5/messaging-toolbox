using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Queries.GetHeldLockStates;

/// <summary>
/// Current expiry (UTC) of held peek-locks, keyed by lock token — only tokens the provider still
/// holds are returned. The UI polls this on its refresh tick so lock countdowns reflect renewals.
/// </summary>
public sealed record GetHeldLockStatesQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue,
    IReadOnlyList<string> LockTokens) : IRequest<IReadOnlyDictionary<string, DateTimeOffset>>;
