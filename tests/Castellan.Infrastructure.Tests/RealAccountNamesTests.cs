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
/// Dwa konta w Revolucie obok dwóch w ING, w dwóch konwencjach nazewniczych, bo obie
/// są naturalne i różnią się skutkami:
///
/// • „Revolut wspólne" / „Revolut osobiste" — nazwa niesie bank, więc powiadomienie
///   z pakietu Revoluta zawęża się do właściwej pary jeszcze przed czytaniem treści;
/// • „wspólne" / „osobiste" — nazwa banku nie pada nigdzie, więc CAŁA robota spada na
///   podpowiedź z treści powiadomienia. Konta ING stoją przy tym alfabetycznie przed
///   kontami Revoluta, więc każde chybienie ląduje na cudzym banku.
///
/// Dopasowanie musi trafiać w obu układach — inaczej poprawność zależy od tego, jak
/// użytkownik nazwał konta, czyli od czegoś, o czym aplikacja nigdzie nie uprzedza.
/// </summary>
public class RealAccountNamesTests
{
    private const string RevolutPackage = "com.revolut.revolut";
    private const string WalletPackage = "com.google.android.apps.walletnfcrel";

    public static TheoryData<string, string> Conventions => new()
    {
        { "Revolut wspólne", "Revolut osobiste" },
        { "wspólne", "osobiste" },
    };

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase useCase)>
        SetupAsync(string dbPath, string joint, string personal)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, Money.Zero, now));
        db.Accounts.Add(Account.Create("OKO ING", AccountKind.Savings, Money.Zero, now));
        db.Accounts.Add(Account.Create(joint, AccountKind.Checking, Money.Zero, now));
        db.Accounts.Add(Account.Create(personal, AccountKind.Checking, Money.Zero, now));
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

    private static async Task<string> AccountNameOfSingleAsync(CastellanDbContext db)
    {
        var tx = await db.Transactions.SingleAsync();
        return (await db.Accounts.SingleAsync(a => a.Id == tx.AccountId)).Name;
    }

    [Theory]
    [MemberData(nameof(Conventions))]
    public async Task Revolut_title_prefix_picks_the_right_account(string joint, string personal)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_real_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, joint, personal);

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Konto wspólne · Allegro", "Wydano 105,50 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfSingleAsync(db)).Should().Be(joint);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Theory]
    [MemberData(nameof(Conventions))]
    public async Task The_personal_account_is_reachable_too(string joint, string personal)
    {
        // Bez tego przypadku test przechodziłby także wtedy, gdyby wszystko lądowało
        // na koncie wspólnym — czyli gdyby wybór w ogóle nie działał.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_real_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, joint, personal);

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Konto osobiste · Lidl", "Wydano 12,30 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfSingleAsync(db)).Should().Be(personal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Theory]
    [MemberData(nameof(Conventions))]
    public async Task Wallet_card_name_reaches_the_same_account_as_the_bank(string joint, string personal)
    {
        // Portfel mówi „karta Revolut Wspólny", bank mówi „Konto wspólne". Dwie różne
        // nazwy tego samego konta — muszą spotkać się na jednym wpisie, inaczej powstaje
        // duplikat.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_real_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, joint, personal);
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                WalletPackage, "Allegro", "Kwota 105,50 zł – karta Revolut Wspólny", now));
            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Konto wspólne · Allegro", "Wydano 105,50 zł.", now.AddSeconds(20)));

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(1, "jedna płatność, dwa powiadomienia");
            (await AccountNameOfSingleAsync(db)).Should().Be(joint);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Theory]
    [MemberData(nameof(Conventions))]
    public async Task Ing_notification_still_finds_its_own_account(string joint, string personal)
    {
        // Kontrola: dopasowanie po słowach nie może przeciągnąć powiadomienia ING
        // na konto Revolut tylko dlatego, że nazwa jest krótsza albo wcześniejsza.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_real_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, joint, personal);

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                "pl.ing.mojeing", "Twój Asystent",
                "105,50 PLN mniej na Twoim koncie - Allegro", DateTimeOffset.UtcNow));

            (await AccountNameOfSingleAsync(db)).Should().Be("ING");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Without_a_prefix_the_bank_in_the_account_name_is_the_only_thing_left()
    {
        // ZNANE OGRANICZENIE, spisane, nie życzenie. Powiadomienie Revoluta bez
        // przedrostka „Konto X ·" nie niesie żadnej informacji o koncie. Gdy nazwa konta
        // zawiera bank, trafia przynajmniej we WŁAŚCIWY BANK (tu: alfabetycznie pierwsze
        // konto Revoluta). Gdy nie zawiera — nie ma się o co oprzeć i ląduje na pierwszym
        // koncie rozliczeniowym w ogóle, czyli na cudzym banku.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_real_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, "Revolut wspólne", "Revolut osobiste");

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Revolut", "Wydano 105,50 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfSingleAsync(db)).Should().Be("Revolut osobiste",
                "bez podpowiedzi zostaje pierwsze konto tego banku — zgadywanie, ale w obrębie banku");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Without_a_prefix_and_without_the_bank_in_the_name_it_lands_elsewhere()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_real_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, "wspólne", "osobiste");

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Revolut", "Wydano 105,50 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfSingleAsync(db)).Should().Be("ING",
                "nazwa konta nie mówi o banku, a treść nie mówi o koncie — nie ma z czego wybrać");
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
