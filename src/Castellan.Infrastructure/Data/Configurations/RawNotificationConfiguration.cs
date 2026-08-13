using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class RawNotificationConfiguration : IEntityTypeConfiguration<RawNotification>
{
    public void Configure(EntityTypeBuilder<RawNotification> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, v => new RawNotificationId(v))
            .ValueGeneratedNever();

        builder.Property(r => r.PackageName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Text).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.PostedAt);
        builder.Property(r => r.ParseStatus).HasConversion<int>();
        builder.Property(r => r.TransactionId)
            .HasConversion(new ValueConverter<TransactionId, Guid>(
                id => id.Value,
                v => new TransactionId(v)));

        builder.HasIndex(r => r.ParseStatus);
        builder.HasIndex(r => r.PostedAt);
        builder.HasIndex(r => r.PackageName);
    }
}
