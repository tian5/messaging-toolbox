using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagingToolbox.Application.Core.Interfaces;

public interface IToolboxDbContext : IAsyncDisposable
{
    DbSet<ConnectionProfile> ConnectionProfiles { get; }

    DbSet<CannedMessage> CannedMessages { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
