using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Explorer.Queries.RefreshCounts;

/// <summary>Runtime counts per entity path for auto-refresh (FR-2.2); empty on connections without a management plane.</summary>
public sealed record RefreshCountsQuery(Guid ConnectionId) : IRequest<IReadOnlyDictionary<string, EntityRuntimeInfoDto>>;
