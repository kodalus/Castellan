using Castellan.Application.Parsers;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Parsers;
using Castellan.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// End-to-end na prawdziwej (migrowanej) bazie: sprawdza, że włączenie parsowania
/// powiadomień Portfela Google nie tworzy duplikatu, gdy ten sam zakup zgłasza
/// też apka banku (przypadek Revolut), oraz że faktycznie tworzy transakcję,
/// gdy Portfel Google jest jedynym źródłem (przypadek ING NFC).
/// </summary>
public class NotificationCaptureIntegrationTests
{
    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase useCase, Account ing, Account revolut)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var ing = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
        var revolut = Account.Create("Revolut", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
        db.Accounts.AddRange(ing, revolut);

        var category = Category.Create("Nieprzypisane", CategoryKind.Expense);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var parsers = new INotificationParser[]
        {
            new IngNotificationParser(),
            new RevolutNotificationParser(),
            new GoogleWalletNotificationParser(),
        };

        var useCase = new IngestRawNotificationUseCase(
            new RawNotificationRepository(db),
            new AccountRepository(db),
            new TransactionRepository(db),
            new CategoryRuleRepository(db),
            new UnitOfWork(db),
            parsers);

        return (db, useCase, ing, revolut);
    }

    [Fact]
    public async Task Wallet_and_bank_notification_for_the_same_purchase_collapse_into_one_transaction()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_notif_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase, _, revolut) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            // Portfel Google fires first (NFC tap confirmation)…
            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                "com.google.android.apps.walletnfcrel", "LIDL 2306",
                "Kwota 136,39 zł – karta Revolut Wspólny", now));

            // …then Revolut's own app notification arrives ~seconds later.
            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                "com.revolut.revolut", "Konto wspólne · Lidl",
                "Wydano 136,39 zł.\nSaldo konta „PLN”: 457,38 zł.", now.AddSeconds(5)));

            var txs = await db.Transactions.Where(t => t.AccountId == revolut.Id).ToListAsync();

            txs.Should().ContainSingle();
            txs[0].Amount.Grosze.Should().Be(-13639);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Wallet_only_notification_is_captured_and_routed_to_the_hinted_account()
    {
        // ING doesn't send its own notification for NFC-via-phone payments —
        // Wallet is the only signal, so it must be enough on its own.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_notif_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase, ing, _) = await SetupAsync(dbPath);

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                "com.google.android.apps.walletnfcrel", "Zabka 4521",
                "Kwota 12,50 zł – karta ING Konto z Lwem", DateTimeOffset.UtcNow));

            var txs = await db.Transactions.ToListAsync();

            txs.Should().ContainSingle();
            txs[0].AccountId.Should().Be(ing.Id);
            txs[0].Amount.Grosze.Should().Be(-1250);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
