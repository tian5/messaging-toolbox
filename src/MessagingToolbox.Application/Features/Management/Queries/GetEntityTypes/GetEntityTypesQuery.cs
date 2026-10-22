using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Management.Queries.GetEntityTypes;

/// <summary>The entity types the connection can create, with their options (ADR 21).</summary>
public sealed record GetEntityTypesQuery(Guid ConnectionId) : IRequest<IReadOnlyList<EntityTypeDescriptor>>;
