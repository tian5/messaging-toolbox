using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MessagingToolbox.Infrastructure.Persistence.Configurations;

public sealed class CannedMessageConfiguration : IEntityTypeConfiguration<CannedMessage>
{
    public void Configure(EntityTypeBuilder<CannedMessage> builder)
    {
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Name).HasMaxLength(200);
        builder.Property(message => message.Folder).HasMaxLength(200);
        builder.Property(message => message.ContentType).HasMaxLength(200);
        builder.Property(message => message.PropertiesJson).HasMaxLength(8000);
    }
}
