using MessagingToolbox.Application.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MessagingToolbox.Infrastructure.Persistence;

public sealed class ToolboxDbContextFactory : IToolboxDbContextFactory
{
    private readonly IDbContextFactory<ToolboxDbContext> _dbContextFactory;

    public ToolboxDbContextFactory(IDbContextFactory<ToolboxDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IToolboxDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
        => await _dbContextFactory.CreateDbContextAsync(cancellationToken);
}
