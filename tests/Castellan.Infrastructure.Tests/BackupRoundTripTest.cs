using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Reguluje regresję: eksport/import wysypywał się na "no store type mapping
/// for properties of type 'DBNull'", bo nullowalne kolumny (RawMerchant,
/// TransferGroupId, BankKey…) trafiały do ExecuteSqlRawAsync jako goły
/// DBNull.Value bez typu. Prawdziwe dane mają te pola puste znacznie
/// częściej niż nasze wcześniejsze testy — stąd realny test z pustymi polami,
/// nie tylko happy-path.
/// </summary>
public class BackupRoundTripTest
{
    [Fact]
    public async Task Export_then_import_survives_transaction_with_all_nullable_fields_empty()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_backup_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new CastellanDbContext(options);
            db.Database.Migrate();

            var account = Account.Create("Konto testowe", AccountKind.Checking, new Money(10_000), DateTimeOffset.UtcNow);
            var category = Category.Create("Testowa", CategoryKind.Expense);
            // CreateManual zostawia RawMerchant, MerchantKey, TransferGroupId,
            // ProposedTransferGroupId, SupersededById, RawNotificationId,
            // PaidFromFundId puste — dokładnie te pola, które wcześniej wysypywały import.
            var transaction = Transaction.CreateManual(account.Id, new Money(-500), DateTimeOffset.UtcNow, category.Id);

            db.Accounts.Add(account);
            db.Categories.Add(category);
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();

            var backup = new BackupService(db);
            var export = await backup.ExportAsync();

            export.Transactions.Should().ContainSingle();

            var act = async () => await backup.ImportAsync(export);
            await act.Should().NotThrowAsync();

            (await db.Transactions.CountAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
