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
/// Koperta ustawiona na zero znika z planu — i dotąd znikała też z ekranu Koperty,
/// o ile w danym miesiącu nic na nią nie poszło. Kategoria, której naprawdę się używa,
/// wypadała przez to po cichu: „Zdrowie i apteka” zgubiło się przy rozrzucaniu wypłaty
/// i brak zauważył się dopiero tydzień później.
///
/// Zakładka Koperty ma odpowiadać na pytanie „co mam w tym miesiącu”, więc wymienia
/// wszystkie kategorie wydatkowe. Pulpit zostaje przy krótszej liście — tam pytanie
/// brzmi „gdzie poszły pieniądze”, a kategoria bez kwoty nie ma na nie odpowiedzi.
/// </summary>
public class UnusedCategoryTests
{
    private const long Plan = 100_000;

    [Fact]
    public async Task A_category_with_no_envelope_and_no_spending_is_still_listed()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, _, zdrowie) = await SetupAsync(dbPath);

            var m = await Overview(db);

            m!.Unused.Should().ContainSingle(e => e.CategoryId == zdrowie);
            m.AllCategories.Should().Contain(e => e.CategoryId == zdrowie);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Zeroing_an_envelope_moves_the_category_to_the_untouched_list()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, zdrowie) = await SetupAsync(dbPath);

            // Dokładnie to, co zrobiła użytkowniczka przy rozrzucaniu wypłaty.
            await PlanAsync(db, (zakupy, Plan), (zdrowie, 7_000));
            (await Overview(db))!.Envelopes.Should().Contain(e => e.CategoryId == zdrowie);

            await PlanAsync(db, (zakupy, Plan));
            var m = await Overview(db);

            m!.Envelopes.Should().NotContain(e => e.CategoryId == zdrowie);
            m.Unused.Should().ContainSingle(e => e.CategoryId == zdrowie,
                "kategoria ma nadal być widoczna, bo inaczej nie da się zauważyć, że wypadła z planu");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task An_untouched_category_changes_no_amount()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, _) = await SetupAsync(dbPath);
            await SpendAsync(db, zakupy, 40_000);

            var m = await Overview(db);

            // Wiersz bez kwoty nie może ruszyć ani wydanych, ani pozostałych do wydania.
            m!.TotalSpent.Grosze.Should().Be(40_000);
            m.RemainingToSpend.Grosze.Should().Be(Plan - 40_000);
            m.TotalUnplanned.Grosze.Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_dashboard_list_stays_short()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy, _) = await SetupAsync(dbPath);

            var m = await Overview(db);

            // Kontrola: pulpit pokazuje koperty i wydatki poza planem, ale nie puste
            // wiersze — inaczej lista na ekranie otwieranym codziennie rosłaby
            // o każdą kategorię, której akurat się nie użyło.
            m!.EnvelopesWithUnplanned.Should().ContainSingle()
                .Which.CategoryId.Should().Be(zakupy);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Archived_and_system_categories_are_left_out()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, _, _) = await SetupAsync(dbPath);

            db.ChangeTracker.Clear();
            var stara = Category.Create("Dawna kategoria", CategoryKind.Expense);
            stara.Archive();
            db.Categories.Add(stara);
            db.Categories.Add(Category.Create("Premia", CategoryKind.Income));
            await db.SaveChangesAsync();

            var m = await Overview(db);

            m!.Unused.Should().NotContain(e => e.CategoryName == "Dawna kategoria");
            m.Unused.Should().NotContain(e => e.CategoryName == "Premia");
            m.Unused.Should().NotContain(e => e.CategoryName == "Nieprzypisane");
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
                YearMonth.Current, new Money(1_000_000),
                [.. koperty.Select(k => new PlanMonthUseCase.EnvelopeInput(k.Id, new Money(k.Kwota)))],
                []));
        db.ChangeTracker.Clear();
    }

    private static async Task SpendAsync(CastellanDbContext db, CategoryId category, long grosze)
    {
        db.ChangeTracker.Clear();
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

        db.Accounts.Add(Account.Create(
            "ING osobisty", AccountKind.Checking, new Money(1_000_000), DateTimeOffset.UtcNow.AddYears(-1)));
        var zakupy  = Category.Create("Produkty do domu", CategoryKind.Expense);
        var zdrowie = Category.Create("Zdrowie i apteka", CategoryKind.Expense);
        db.Categories.AddRange(zakupy, zdrowie);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await PlanAsync(db, (zakupy.Id, Plan));

        return (db, zakupy.Id, zdrowie.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_nietkniete_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
