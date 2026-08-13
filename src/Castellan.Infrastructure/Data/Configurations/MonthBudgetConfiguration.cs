using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class MonthBudgetConfiguration : IEntityTypeConfiguration<MonthBudget>
{
    public void Configure(EntityTypeBuilder<MonthBudget> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, v => new MonthBudgetId(v))
            .ValueGeneratedNever();
        builder.Property(b => b.Month)
            .HasConversion(new ValueConverter<YearMonth, string>(
                m => m.ToString(),
                s => new YearMonth(int.Parse(s.Substring(0, 4)), int.Parse(s.Substring(5, 2)))));
        builder.Property(b => b.AvailableFunds)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(b => b.PlannedAt);

        builder.HasMany(b => b.Envelopes)
            .WithOne()
            .HasForeignKey(e => e.MonthBudgetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Envelopes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(b => b.Month).IsUnique();
    }
}
