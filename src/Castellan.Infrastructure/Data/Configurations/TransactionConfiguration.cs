using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Castellan.Infrastructure.Data.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, v => new TransactionId(v))
            .ValueGeneratedNever();
        builder.Property(t => t.AccountId)
            .HasConversion(id => id.Value, v => new AccountId(v));
        builder.Property(t => t.Amount)
            .HasConversion(m => m.Grosze, v => new Money(v));
        builder.Property(t => t.OccurredAt);
        builder.Property(t => t.CategoryId)
            .HasConversion(id => id.Value, v => new CategoryId(v));
        builder.Property(t => t.RawMerchant).HasMaxLength(500);
        builder.Property(t => t.MerchantKey).HasMaxLength(200);
        builder.Property(t => t.Note).HasMaxLength(1000);
        builder.Property(t => t.Source).HasConversion<int>();
        builder.Property(t => t.Kind).HasConversion<int>();
        builder.Property(t => t.TransferGroupId);
        // EF Core handles null wrapping automatically; converter receives non-null values only
        builder.Property(t => t.SupersededById)
            .HasConversion(new ValueConverter<TransactionId, Guid>(
                id => id.Value,
                v => new TransactionId(v)));
        builder.Property(t => t.RawNotificationId);

        builder.Ignore(t => t.IsExcludedFromCalculations);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.AccountId);
        builder.HasIndex(t => t.OccurredAt);
        builder.HasIndex(t => t.CategoryId);
    }
}
