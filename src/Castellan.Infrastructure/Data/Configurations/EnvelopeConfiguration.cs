using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class EnvelopeConfiguration : IEntityTypeConfiguration<Envelope>
{
    public void Configure(EntityTypeBuilder<Envelope> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id);
        builder.Property(e => e.MonthBudgetId);
        builder.Property(e => e.CategoryId)
            .HasConversion(id => id.Value, v => new CategoryId(v));
        builder.Property(e => e.PlannedAmount)
            .HasConversion(m => m.Grosze, v => new Money(v));
    }
}
