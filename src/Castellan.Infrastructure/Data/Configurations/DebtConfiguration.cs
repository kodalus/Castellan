using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class DebtConfiguration : IEntityTypeConfiguration<Debt>
{
    public void Configure(EntityTypeBuilder<Debt> builder)
    {
        builder.ToTable("Debts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, v => new DebtId(v));

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(d => d.InitialAmount)
            .HasConversion(m => m.Grosze, v => new Money(v))
            .IsRequired();

        builder.Property(d => d.Balance)
            .HasConversion(m => m.Grosze, v => new Money(v))
            .IsRequired();

        builder.Property(d => d.InstallmentAmount)
            .HasConversion(m => m.Grosze, v => new Money(v))
            .IsRequired();

        builder.Property(d => d.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);

        // Wyliczane z salda i raty — nie ma ich w bazie.
        builder.Ignore(d => d.IsPaidOff);
        builder.Ignore(d => d.Progress);
        builder.Ignore(d => d.PaidOff);
        builder.Ignore(d => d.InstallmentsRemaining);
    }
}
