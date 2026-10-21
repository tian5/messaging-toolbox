using Mediator;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MessagingToolbox.Application.Features.Audit.Queries.GetAuditEntries;

public sealed class GetAuditEntriesQueryHandler : IRequestHandler<GetAuditEntriesQuery, IReadOnlyList<AuditEntryDto>>
{
    private const int MaxEntries = 100;

    private readonly IToolboxDbContextFactory _dbContextFactory;

    public GetAuditEntriesQueryHandler(IToolboxDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async ValueTask<IReadOnlyList<AuditEntryDto>> Handle(GetAuditEntriesQuery request, CancellationToken cancellationToken)
    {
        await using IToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        List<AuditEntryDto> auditEntries = await dbContext.AuditEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(MaxEntries)
            .Select(entry => new AuditEntryDto(
                entry.CreatedAt,
                entry.Action,
                entry.ConnectionName,
                entry.EntityPath,
                entry.SubQueue.ToString(),
                entry.Details))
            .ToListAsync(cancellationToken);

        return auditEntries;
    }
}
