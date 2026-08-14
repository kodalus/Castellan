using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, v => new AssetId(v));

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Liquidity)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.Value)
            .HasConversion(m => m.Grosze, v => new Money(v))
            .IsRequired();

        builder.Property(a => a.UpdatedOn)
            .HasConversion(
                d => d.ToString("yyyy-MM-dd"),
                v => DateOnly.ParseExact(v, "yyyy-MM-dd", null))
            .IsRequired();

        builder.Property(a => a.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);
    }
}
