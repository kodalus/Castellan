using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class MonthBudgetConfiguration : IEntityTypeConfiguration<MonthBudget>
{
    public void Configure(EntityTypeBuilder<MonthBudget> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, v => new MonthBudgetId(v));
        builder.Property(b => b.Month)
            .HasConversion(
                m => m.ToString(),
                s => { var p = s.Split('-'); return new YearMonth(int.Parse(p[0]), int.Parse(p[1])); });
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
