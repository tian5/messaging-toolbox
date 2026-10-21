using System.Reflection;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MessagingToolbox.Infrastructure.Persistence;

/// <summary>
/// Toolbox state store. Auditing is centralized here: CreatedAt/ModifiedAt are stamped from the
/// injected TimeProvider on every save (code style conventions §6).
/// </summary>
public class ToolboxDbContext : DbContext, IToolboxDbContext
{
    private readonly TimeProvider _timeProvider;

    public ToolboxDbContext(DbContextOptions<ToolboxDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
    }

    public DbSet<ConnectionProfile> ConnectionProfiles { get; set; } = null!;

    public DbSet<CannedMessage> CannedMessages { get; set; } = null!;

    public DbSet<AuditEntry> AuditEntries { get; set; } = null!;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditableEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsSqlite())
        {
            // SQLite does not have proper support for DateTimeOffset via Entity Framework Core, see the limitations
            // here: https://docs.microsoft.com/en-us/ef/core/providers/sqlite/limitations#query-limitations
            // To work around this, when the Sqlite database provider is used, all model properties of type DateTimeOffset
            // use the DateTimeOffsetToBinaryConverter
            // Based on: https://github.com/aspnet/EntityFrameworkCore/issues/10784#issuecomment-415769754
            // This only supports millisecond precision.
            foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
            {
                IEnumerable<PropertyInfo> properties = entityType.ClrType
                    .GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));

                foreach (PropertyInfo property in properties)
                {
                    modelBuilder
                        .Entity(entityType.Name)
                        .Property(property.Name)
                        .HasConversion(new DateTimeOffsetToBinaryConverter());
                }
            }
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ToolboxDbContext).Assembly);
    }

    private void StampAuditableEntities()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (EntityEntry<EntityBase> entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.ModifiedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
            }
        }
    }
}
