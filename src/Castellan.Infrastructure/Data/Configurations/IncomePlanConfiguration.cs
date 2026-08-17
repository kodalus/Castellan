using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class IncomePlanConfiguration : IEntityTypeConfiguration<IncomePlan>
{
    public void Configure(EntityTypeBuilder<IncomePlan> builder)
    {
        builder.ToTable("IncomePlans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id);
        builder.Property(p => p.MonthBudgetId)
            .HasConversion(id => id.Value, v => new MonthBudgetId(v));
        builder.Property(p => p.CategoryId)
            .HasConversion(id => id.Value, v => new CategoryId(v));
        builder.Property(p => p.PlannedAmount)
            .HasConversion(m => m.Grosze, v => new Money(v));
    }
}
