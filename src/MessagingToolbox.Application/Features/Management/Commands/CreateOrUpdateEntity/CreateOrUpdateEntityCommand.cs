using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateOrUpdateEntity;

public sealed record CreateOrUpdateEntityCommand(
    Guid ConnectionId,
    string Path,
    EntityKind Kind,
    string? ParentPath,
    IReadOnlyDictionary<string, object?> Options) : IRequest<string>;
