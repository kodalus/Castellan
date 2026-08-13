using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, v => new AccountId(v))
            .ValueGeneratedNever();
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.BankKey).HasMaxLength(100);
        builder.Property(a => a.Kind).HasConversion<int>();
        builder.Property(a => a.LiquidityTier).HasConversion<int>();
        builder.Property(a => a.LastReconciledBalance)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(a => a.LastReconciledAt);
        builder.Property(a => a.IsArchived);
    }
}
