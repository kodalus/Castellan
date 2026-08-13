using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class ReconciliationConfiguration : IEntityTypeConfiguration<Reconciliation>
{
    public void Configure(EntityTypeBuilder<Reconciliation> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, v => new ReconciliationId(v))
            .ValueGeneratedNever();
        builder.Property(r => r.AccountId)
            .HasConversion(id => id.Value, v => new AccountId(v));
        builder.Property(r => r.ObservedBalance)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(r => r.ObservedAt);
        builder.Property(r => r.PreviousBalance)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(r => r.PreviousAt);
        builder.Property(r => r.RecordedDelta)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(r => r.Discrepancy)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(r => r.GeneratedTransactionId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                v => v.HasValue ? new TransactionId(v.Value) : null);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.AccountId);
        builder.HasIndex(r => r.ObservedAt);
    }
}
