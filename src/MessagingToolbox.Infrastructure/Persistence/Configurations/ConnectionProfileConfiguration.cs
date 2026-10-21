using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MessagingToolbox.Infrastructure.Persistence.Configurations;

public sealed class ConnectionProfileConfiguration : IEntityTypeConfiguration<ConnectionProfile>
{
    public void Configure(EntityTypeBuilder<ConnectionProfile> builder)
    {
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.DisplayName).HasMaxLength(200);
        builder.Property(profile => profile.Kind).HasConversion<string>().HasMaxLength(50);
        builder.Property(profile => profile.ProtectedConnectionString).HasMaxLength(4000);
    }
}
