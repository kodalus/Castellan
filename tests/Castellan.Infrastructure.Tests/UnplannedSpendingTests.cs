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
/// Wydatek w kategorii bez koperty też jest wydatkiem.
///
/// „Wydane” sumowało wyłącznie koperty, a koperta ustawiona na zero znika z planu.
/// Wystarczyło więc wyzerować kopertę przy przeplanowaniu miesiąca, żeby RAZEM Z NIĄ
/// z pulpitu zniknęły wszystkie wydatki tej kategorii — u użytkowniczki 3 259,97 zł
/// (dentysta, apteka, transport) przestało się liczyć w chwili, gdy przesuwała
/// pieniądze między kopertami. Ekran pokazywał wtedy MNIEJ wydane, niż było naprawdę.
/// To gorszy kierunek błędu niż zawyżanie: uspokaja.
/// </summary>
public class UnplannedSpendingTests
{
    private const long Plan     = 100_000;
    private const long Zakupy   = 40_000;
    private const long Dentysta = 196_297;

    [Fact]
    public async Task Spending_without_an_envelope_counts_as_spent()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, zdrowie) = await SetupAsync(dbPath);
            await SpendAsync(db, zakupy, Zakupy);
            await SpendAsync(db, zdrowie, Dentysta);

            var m = await Overview(db);

            m!.TotalSpent.Grosze.Should().Be(Zakupy + Dentysta);
            m.TotalUnplanned.Grosze.Should().Be(Dentysta);
            m.RemainingToSpend.Grosze.Should().Be(Plan - Zakupy - Dentysta);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Zeroing_an_envelope_does_not_hide_what_was_already_spent()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, zdrowie) = await SetupAsync(dbPath);
            await SpendAsync(db, zakupy, Zakupy);
            await SpendAsync(db, zdrowie, Dentysta);

            // Koperta „Zdrowie" dostaje plan, a potem zostaje wyzerowana — czyli wypada
            // z planu, dokładnie tak jak przy przeplanowaniu miesiąca na ekranie Koperty.
            await PlanAsync(db, (zakupy, Plan), (zdrowie, 7_000));
            var zPlanem = await Overview(db);
            await PlanAsync(db, (zakupy, Plan));
            var bezPlanu = await Overview(db);

            zPlanem!.TotalSpent.Grosze.Should().Be(Zakupy + Dentysta);
            bezPlanu!.TotalSpent.Grosze.Should().Be(Zakupy + Dentysta,
                "wyzerowanie koperty zmienia plan, a nie to, co już wyszło z konta");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Unplanned_spending_shows_up_as_its_own_row()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, zdrowie) = await SetupAsync(dbPath);
            await SpendAsync(db, zakupy, Zakupy);
            await SpendAsync(db, zdrowie, Dentysta);

            var m = await Overview(db);

            // Sama liczba w sumie nie wystarczy: bez wiersza na liście nie da się
            // dojść, skąd się wzięła, ani czego brakuje w planie.
            var wiersz = m!.Unplanned.Should().ContainSingle().Which;
            wiersz.CategoryName.Should().Be("Zdrowie i apteka");
            wiersz.IsUnplanned.Should().BeTrue();
            wiersz.IsOverBudget.Should().BeTrue();
            m.EnvelopesWithUnplanned.Should().HaveCount(m.Envelopes.Count + 1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refund_in_a_category_without_an_envelope_is_not_spending()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, zdrowie) = await SetupAsync(dbPath);
            await SpendAsync(db, zakupy, Zakupy);
            await SpendAsync(db, zdrowie, -5_000); // zwrot: pieniądze wróciły

            var m = await Overview(db);

            // Kategoria, w której jest sam zwrot, nie ma czego dopisać do „wydane”.
            m!.TotalUnplanned.Grosze.Should().Be(0);
            m.Unplanned.Should().BeEmpty();
            m.TotalSpent.Grosze.Should().Be(Zakupy);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Income_without_a_plan_is_not_turned_into_spending()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, _) = await SetupAsync(dbPath);
            await SpendAsync(db, zakupy, Zakupy);

            var premia = Category.Create("Premia", CategoryKind.Income);
            db.Categories.Add(premia);
            await db.SaveChangesAsync();
            // Korekta przychodu na minus to nadal przychód, a nie zakupy.
            await SpendAsync(db, premia.Id, 30_000);

            var m = await Overview(db);

            m!.TotalUnplanned.Grosze.Should().Be(0);
            m.TotalSpent.Grosze.Should().Be(Zakupy);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static Task<MonthOverview?> Overview(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        return new GetMonthOverviewUseCase(
            new MonthBudgetRepository(db), new CategoryRepository(db), new TransactionRepository(db))
            .ExecuteAsync(YearMonth.Current);
    }

    private static async Task PlanAsync(CastellanDbContext db, params (CategoryId Id, long Kwota)[] koperty)
    {
        db.ChangeTracker.Clear();
        await new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
            new PlanMonthUseCase.Input(
                YearMonth.Current,
                new Money(1_000_000),
                [.. koperty.Select(k => new PlanMonthUseCase.EnvelopeInput(k.Id, new Money(k.Kwota)))],
                []));
        db.ChangeTracker.Clear();
    }

    private static async Task SpendAsync(CastellanDbContext db, CategoryId category, long grosze)
    {
        var account = await db.Accounts.FirstAsync();
        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(-grosze), DateTimeOffset.Now, category));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<(CastellanDbContext db, CategoryId zakupy, CategoryId zdrowie)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, new Money(500_000), DateTimeOffset.UtcNow.AddYears(-1)));
        var zakupy  = Category.Create("Produkty do domu", CategoryKind.Expense);
        var zdrowie = Category.Create("Zdrowie i apteka", CategoryKind.Expense);
        db.Categories.AddRange(zakupy, zdrowie);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Plan miesiąca: tylko zakupy. „Zdrowie i apteka" nie ma koperty.
        await PlanAsync(db, (zakupy.Id, Plan));

        return (db, zakupy.Id, zdrowie.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_pozaplanem_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
