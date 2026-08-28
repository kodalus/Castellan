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
using Xunit.Abstractions;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Zgłoszony duplikat: Castorama 112,90 zł. Portfel Google podaje „CASTORAMA
/// TARNOWSKIEG8", Revolut „Castorama" — klucze sprzedawcy są RÓŻNE, więc dopasowanie
/// po nazwie nie ma szans i wszystko zależy od tego, czy oba powiadomienia trafią
/// na to samo konto.
/// </summary>
public class CastoramaDuplicateTests(ITestOutputHelper output)
{
    private const string RevolutPackage = "com.revolut.revolut";
    private const string WalletPackage = "com.google.android.apps.walletnfcrel";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest)>
        SetupAsync(string dbPath, (string Name, string? Bank)[] accounts)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        foreach (var (name, bank) in accounts)
            db.Accounts.Add(Account.Create(name, AccountKind.Checking, Money.Zero, now, bank));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, new IngestRawNotificationUseCase(
            new RawNotificationRepository(db), new AccountRepository(db),
            new TransactionRepository(db), new CategoryRuleRepository(db), new UnitOfWork(db),
            [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]));
    }

    private static IngestRawNotificationUseCase.Input Wallet(DateTimeOffset at) =>
        new(WalletPackage, "CASTORAMA TARNOWSKIEG8", "Kwota 112,90 zł – karta Revolut Wspólny", at);

    private static IngestRawNotificationUseCase.Input Revolut(DateTimeOffset at) =>
        new(RevolutPackage, "Konto wspólne · Castorama",
            "Wydano 112,90 zł.\nSaldo konta „PLN”: 1 996,79 zł.", at);

    public static TheoryData<string, string, string?> Setups => new()
    {
        // nazwa wspolnego, nazwa osobistego, bank ustawiony przy obu
        { "Revolut Wspólny", "Revolut Osobiste", null },
        { "Revolut Wspólny", "Revolut Osobiste", Banks.Revolut },
        { "wspólne",         "osobiste",         Banks.Revolut },
        { "wspólne",         "osobiste",         null },
        { "Wspólne",         "Revolut",          null },
        { "Wspólne",         "Revolut",          Banks.Revolut },
    };

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task One_purchase_two_notifications_stays_one_transaction(
        string joint, string personal, string? bank)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cast_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest) = await SetupAsync(dbPath,
                [(joint, bank), (personal, bank), ("ING", Banks.Ing)]);

            var now = DateTimeOffset.UtcNow;
            await ingest.ExecuteAsync(Wallet(now));
            await ingest.ExecuteAsync(Revolut(now.AddSeconds(20)));
            db.ChangeTracker.Clear();

            var txs = await db.Transactions.ToListAsync();
            var accounts = await db.Accounts.ToListAsync();
            foreach (var t in txs)
                output.WriteLine($"{accounts.First(a => a.Id == t.AccountId).Name,-18} " +
                                 $"{t.Amount.Grosze,8} key={t.MerchantKey}");

            txs.Count.Should().Be(1, "jedna płatność, dwa powiadomienia");

            // Gdy bank jest wskazany przy koncie, wiadomo też, KTÓRE to konto: nazwa banku
            // przestaje rozstrzygać wewnątrz jednego banku, więc podpowiedź „karta Revolut
            // Wspólny" trafia we „Wspólne", a nie w konto nazwane po prostu „Revolut".
            //
            // Bez wskazanego banku takiej pewności nie ma i test tego nie udaje.
            if (bank is not null)
            {
                var landed = accounts.First(a => a.Id == txs[0].AccountId).Name;
                landed.Should().Be(joint);
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
