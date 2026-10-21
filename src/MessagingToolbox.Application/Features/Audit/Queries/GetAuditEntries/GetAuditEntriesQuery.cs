using Mediator;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Audit.Queries.GetAuditEntries;

/// <summary>Latest destructive-operation audit records for the in-app audit panel (NFR-3).</summary>
public sealed record GetAuditEntriesQuery : IRequest<IReadOnlyList<AuditEntryDto>>;
