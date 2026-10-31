using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Odłożenie na rezerwę nie jest wydatkiem — pieniądze nie zostały skonsumowane, tylko
/// przeniesione. Liczone razem z zakupami czyniły „wydatki” miarą bez sensu: miesiąc,
/// w którym odłożono 19 000 zł, wyglądał na katastrofę wydatkową, a „Rezerwy” stały na
/// pierwszym miejscu listy „na co wydaję” i wypychały z niej prawdziwe kategorie.
///
/// Samo wykluczenie byłoby jednak drugim zafałszowaniem: odłożenie JEST prawdziwym
/// ubytkiem z miesiąca, więc netto musi je dalej odejmować — inaczej ekran pokazałby
/// nadwyżkę w miesiącu, w którym konto realnie zjechało w dół.
/// </summary>
public class StatisticsReserveTests
{
    private const long Zakupy    = 30_000;
    private const long Wyplata   = 500_000;
    private const long Odlozone  = 190_000;
    private const long Wyjete    = 90_000;

    [Fact]
    public async Task Putting_money_aside_is_not_counted_as_spending()
    {
        var dbPath = NewDbPath();
        try
        {
            var db = await SetupAsync(dbPath);

            var m = (await Stats(db)).Months.Last();

            m.Expense.Grosze.Should().Be(Zakupy);
            m.Saved.Grosze.Should().Be(Odlozone - Wyjete, "liczy się odłożone NETTO");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_net_still_subtracts_what_was_put_aside()
    {
        var dbPath = NewDbPath();
        try
        {
            var db = await SetupAsync(dbPath);

            // Kontrola: wyłączenie rezerw z wydatków nie może zrobić z miesiąca
            // nadwyżkowego, skoro pieniądze naprawdę z konta zeszły.
            var m = (await Stats(db)).Months.Last();

            m.Net.Grosze.Should().Be(Wyplata - Zakupy - (Odlozone - Wyjete));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Reserves_do_not_crowd_out_the_real_categories()
    {
        var dbPath = NewDbPath();
        try
        {
            var db = await SetupAsync(dbPath);

            var top = (await Stats(db)).TopExpenseCategories;

            top.Should().NotContain(c => c.CategoryName == ConfirmTransferUseCase.ReserveCategoryName);
            top.Should().ContainSingle().Which.CategoryName.Should().Be("Produkty do domu");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Taking_more_out_than_was_put_in_shows_as_negative_saving()
    {
        var dbPath = NewDbPath();
        try
        {
            var db = await SetupAsync(dbPath);
            // Jeszcze jedno wyjęcie — w tym miesiącu z rezerwy ubyło więcej, niż przybyło.
            await AddAsync(db, ConfirmTransferUseCase.ReserveCategoryName, Odlozone);

            var m = (await Stats(db)).Months.Last();

            m.Saved.Grosze.Should().Be(-Wyjete);
            // I ta kwota wraca do dyspozycji, więc netto rośnie zamiast maleć.
            m.Net.Grosze.Should().Be(Wyplata - Zakupy + Wyjete);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static Task<MonthlyStats> Stats(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        return new GetMonthlyStatsUseCase(new TransactionRepository(db), new CategoryRepository(db))
            .ExecuteAsync(YearMonth.Current, monthCount: 1);
    }

    private static async Task AddAsync(CastellanDbContext db, string kategoria, long grosze)
    {
        db.ChangeTracker.Clear();
        var account = await db.Accounts.FirstAsync();
        var category = await db.Categories.FirstAsync(c => c.Name == kategoria);
        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(grosze), DateTimeOffset.Now, category.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<CastellanDbContext> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        db.Accounts.Add(Account.Create(
            "ING osobisty", AccountKind.Checking, new Money(1_000_000), DateTimeOffset.UtcNow.AddYears(-1)));
        db.Categories.AddRange(
            Category.Create("Produkty do domu", CategoryKind.Expense),
            Category.Create(ConfirmTransferUseCase.ReserveCategoryName, CategoryKind.Expense),
            Category.Create("Wypłata", CategoryKind.Income));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await AddAsync(db, "Produkty do domu", -Zakupy);
        await AddAsync(db, "Wypłata", Wyplata);
        // Odkładanie ma kwotę ujemną, wyjmowanie dodatnią — tak samo jak na kontach.
        await AddAsync(db, ConfirmTransferUseCase.ReserveCategoryName, -Odlozone);
        await AddAsync(db, ConfirmTransferUseCase.ReserveCategoryName, Wyjete);

        return db;
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_statystyki_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
