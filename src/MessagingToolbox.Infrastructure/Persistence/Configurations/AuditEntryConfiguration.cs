using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MessagingToolbox.Infrastructure.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Action).HasMaxLength(100);
        builder.Property(entry => entry.ConnectionName).HasMaxLength(200);
        builder.Property(entry => entry.EntityPath).HasMaxLength(500);
        builder.Property(entry => entry.SubQueue).HasConversion<string>().HasMaxLength(50);
        builder.Property(entry => entry.Details).HasMaxLength(2000);
        builder.HasIndex(entry => entry.CreatedAt);
    }
}
