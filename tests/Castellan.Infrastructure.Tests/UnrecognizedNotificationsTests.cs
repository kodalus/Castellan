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
/// Powiadomienie, którego parser nie rozumie, było zapisywane i przepadało bez śladu —
/// żaden ekran nie odwoływał się do ParseStatus. Tak przez wiele tygodni gubiły się
/// przelewy własne ING i nic nigdy by tego nie zgłosiło.
///
/// Lista jest zawężona do powiadomień Z KWOTĄ. ING i Revolut przysyłają też reklamy
/// i przypomnienia o logowaniu; bez tego filtra lista zalałaby się szumem i przestano
/// by na nią patrzeć — czyli byłaby tak samo bezużyteczna jak jej brak.
/// </summary>
public class UnrecognizedNotificationsTests
{
    private const string IngPackage = "pl.ing.mojeing";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest,
        GetUnrecognizedNotificationsUseCase list, IgnoreRawNotificationUseCase ignore)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, new Money(100_000),
            DateTimeOffset.UtcNow.AddYears(-1), Banks.Ing));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rawRepo = new RawNotificationRepository(db);
        return (db,
            new IngestRawNotificationUseCase(
                rawRepo, new AccountRepository(db), new TransactionRepository(db),
                new CategoryRuleRepository(db), new UnitOfWork(db),
                [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]),
            new GetUnrecognizedNotificationsUseCase(rawRepo),
            new IgnoreRawNotificationUseCase(rawRepo, new UnitOfWork(db)));
    }

    private static IngestRawNotificationUseCase.Input Notification(string text) =>
        new(IngPackage, "Moje ING. Twój Asystent", text, DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_notification_with_money_that_nothing_understood_shows_up()
    {
        // Format, ktorego zaden parser nie zna, ale ktory wyraznie dotyczy pieniedzy.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_unrec_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, list, _) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(Notification("Zablokowano 249,00 PLN na Twojej karcie"));
            db.ChangeTracker.Clear();

            (await db.Transactions.CountAsync()).Should().Be(0, "parser tego nie rozumie");

            var row = (await list.ExecuteAsync()).Should().ContainSingle().Subject;
            row.SourceName.Should().Be("ING");
            row.Amount.Grosze.Should().Be(24_900, "kwota to jedyne, co da sie odczytac na pewno");
            row.Text.Should().Contain("Zablokowano");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Notifications_without_an_amount_stay_out_of_the_list()
    {
        // Reklamy i przypomnienia o logowaniu tez sa nierozpoznane, ale nie o nie chodzi.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_unrec_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, list, _) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(Notification("Zaloguj sie, zeby potwierdzic urzadzenie"));
            await ingest.ExecuteAsync(Notification("Nowa oferta kredytowa czeka w aplikacji"));
            db.ChangeTracker.Clear();

            (await db.RawNotifications.CountAsync()).Should().Be(2, "tresc jest zachowana");
            (await list.ExecuteAsync()).Should().BeEmpty("ale lista pokazuje tylko to, co dotyczy pieniedzy");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_notification_that_produced_a_transaction_never_appears()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_unrec_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, list, _) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(Notification("69,57 PLN mniej na Twoim koncie - Biedronka"));
            db.ChangeTracker.Clear();

            (await db.Transactions.CountAsync()).Should().Be(1);
            (await list.ExecuteAsync()).Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Setting_one_aside_takes_it_off_the_list_for_good()
    {
        // Bez odkladania lista rosnie bez konca i szybko przestaje sie na nia patrzec.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_unrec_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, list, ignore) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(Notification("Zablokowano 249,00 PLN na Twojej karcie"));
            db.ChangeTracker.Clear();

            var row = (await list.ExecuteAsync()).Single();
            await ignore.ExecuteAsync(row.Id);
            db.ChangeTracker.Clear();

            (await list.ExecuteAsync()).Should().BeEmpty();
            (await db.RawNotifications.SingleAsync()).ParseStatus.Should().Be(ParseStatus.Ignored,
                "tresc zostaje w bazie — odlozenie to nie kasowanie");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Setting_aside_something_already_gone_is_harmless()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_unrec_{Guid.NewGuid():N}.db");
        try
        {
            var (_, _, _, ignore) = await SetupAsync(dbPath);

            var act = async () => await ignore.ExecuteAsync(RawNotificationId.New());
            await act.Should().NotThrowAsync();
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
