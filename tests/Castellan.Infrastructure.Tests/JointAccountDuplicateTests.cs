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
/// Dwa konta w TYM SAMYM banku. Pakiet powiadomienia mówi tylko „to Revolut", więc
/// samo w sobie nie rozstrzyga, z którego konta poszła płatność — rozstrzyga dopiero
/// treść: Revolut pisze „Konto wspólne · Allegro", Portfel Google „karta Revolut Wspólny".
///
/// Gdy podpowiedź jest ignorowana, wszystkie powiadomienia z banku lądują na jednym
/// koncie, a para bank + Portfel rozjeżdża się na dwa różne konta i deduplikacja nie
/// ma czego z czym porównać. Objawem jest duplikat, ale przyczyną jest błędne konto.
/// </summary>
public class JointAccountDuplicateTests
{
    private const string RevolutPackage = "com.revolut.revolut";
    private const string WalletPackage = "com.google.android.apps.walletnfcrel";

    private const string Personal = "Revolut";
    private const string Joint = "Revolut Wspólny";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase useCase)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        // Konta wracają posortowane po nazwie, więc „Revolut" wypada przed
        // „Revolut Wspólny". Dawny wybór po samej nazwie banku brał pierwsze trafienie,
        // czyli zawsze konto osobiste — testy muszą to widzieć, a nie trafiać w dobre
        // konto przypadkiem.
        db.Accounts.Add(Account.Create(Personal, AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1)));
        db.Accounts.Add(Account.Create(Joint, AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1)));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var useCase = new IngestRawNotificationUseCase(
            new RawNotificationRepository(db),
            new AccountRepository(db),
            new TransactionRepository(db),
            new CategoryRuleRepository(db),
            new UnitOfWork(db),
            [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]);

        return (db, useCase);
    }

    private static IngestRawNotificationUseCase.Input RevolutNotification(DateTimeOffset at) =>
        new(RevolutPackage, "Konto wspólne · Allegro",
            "Wydano 105,50 zł.\nSaldo konta „PLN”: 2 918,21 zł.", at);

    private static IngestRawNotificationUseCase.Input WalletNotification(DateTimeOffset at) =>
        new(WalletPackage, "Allegro", "Kwota 105,50 zł – karta Revolut Wspólny", at);

    private static async Task<string> AccountNameOfSingleAsync(CastellanDbContext db)
    {
        var tx = await db.Transactions.SingleAsync();
        return (await db.Accounts.SingleAsync(a => a.Id == tx.AccountId)).Name;
    }

    [Fact]
    public async Task Revolut_notification_lands_on_the_account_named_in_its_title()
    {
        // „Konto wspólne" kontra „Revolut Wspólny": żadna z nazw nie zawiera drugiej,
        // więc dopasowanie musi iść po słowach, nie po zawieraniu całych nazw.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_joint_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);

            await useCase.ExecuteAsync(RevolutNotification(DateTimeOffset.UtcNow));

            (await AccountNameOfSingleAsync(db)).Should().Be(Joint,
                "z konta wspólnego, nie z pierwszego konta Revolut w kolejności alfabetycznej");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Wallet_and_bank_report_one_purchase_as_one_transaction()
    {
        // Dokładnie zgłoszony przypadek: Allegro za 105,50 zł kartą Revolut Wspólny.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_joint_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(WalletNotification(now));
            await useCase.ExecuteAsync(RevolutNotification(now.AddSeconds(20)));

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(1);
            (await AccountNameOfSingleAsync(db)).Should().Be(Joint);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Order_of_the_two_notifications_does_not_matter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_joint_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(RevolutNotification(now));
            await useCase.ExecuteAsync(WalletNotification(now.AddSeconds(20)));

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Different_merchants_on_different_accounts_stay_separate()
    {
        // Kontrola do złagodzonego warunku na koncie. Ta sama kwota, dwa różne źródła,
        // ale inny sprzedawca i inne konto — czyli para, której źADEN warunek nie łączy.
        // Gdyby złagodzenie poszło za daleko i zostało samo „kwota + inne źródło",
        // te dwie prawdziwe płatności zlepiłyby się w jedną.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_joint_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                WalletPackage, "Biedronka", "Kwota 105,50 zł – karta Revolut", now));
            await useCase.ExecuteAsync(RevolutNotification(now.AddMinutes(40)));

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(2);
            (await db.Accounts.ToListAsync()).Select(a => a.Name).Should().Contain([Personal, Joint]);
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
