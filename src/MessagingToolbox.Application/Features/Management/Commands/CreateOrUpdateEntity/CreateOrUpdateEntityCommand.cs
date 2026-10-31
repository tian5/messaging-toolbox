using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateOrUpdateEntity;

/// <summary>Creates or updates an entity by name; the provider builds its path (ADR 21).</summary>
public sealed record CreateOrUpdateEntityCommand(
    Guid ConnectionId,
    string Name,
    EntityKind Kind,
    string? ParentPath,
    IReadOnlyDictionary<string, object?> Options) : IRequest<string>;
