using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("Funds");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, v => new FundId(v));

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(f => f.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(f => f.TargetAmount)
            .HasConversion(m => m.Grosze, v => new Money(v))
            .IsRequired();

        builder.Property(f => f.Balance)
            .HasConversion(m => m.Grosze, v => new Money(v))
            .IsRequired();

        builder.Property(f => f.StartMonth)
            .HasConversion(
                d => d.ToString("yyyy-MM-dd"),
                v => DateOnly.ParseExact(v, "yyyy-MM-dd", null))
            .IsRequired();

        builder.Property(f => f.Deadline)
            .HasConversion(
                d => d.ToString("yyyy-MM-dd"),
                v => DateOnly.ParseExact(v, "yyyy-MM-dd", null))
            .IsRequired();

        builder.Property(f => f.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);
    }
}
