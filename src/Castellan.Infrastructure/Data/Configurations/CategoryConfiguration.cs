using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, v => new CategoryId(v));
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Kind).HasConversion<int>();
        builder.Property(c => c.IsSystem);
        builder.Property(c => c.IsArchived);
    }
}
