using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

public class CategoryRuleConfiguration : IEntityTypeConfiguration<CategoryRule>
{
    public void Configure(EntityTypeBuilder<CategoryRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, v => new CategoryRuleId(v))
            .ValueGeneratedNever();
        builder.Property(r => r.CategoryId)
            .HasConversion(id => id.Value, v => new CategoryId(v))
            .ValueGeneratedNever();
        builder.Property(r => r.Pattern).IsRequired().HasMaxLength(200);
        builder.HasIndex(r => r.Priority);
    }
}
