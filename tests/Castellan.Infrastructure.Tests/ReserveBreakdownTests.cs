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
/// Kwota koperty jest NETTO i jedna liczba nie mowi, z czego powstala.
///
/// Na zwyklej kategorii to bez znaczenia — zwrot zdarza sie raz na kwartal. W „Rezerwach"
/// ruch idzie w obie strony co tydzien: w listopadzie poszlo tam 7 200 zl, a 2 400 zl
/// wrocilo. Koperta pokazywala „4 800,00 z 9 000,00", co czyta sie jak „odlozylam polowe
/// planu", i ta liczba zmylila uzytkowniczke dwa razy pod rzad.
/// </summary>
public class ReserveBreakdownTests
{
    private const long Plan = 900_000;

    [Fact]
    public async Task The_reserve_envelope_says_how_much_went_in_and_how_much_came_back()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (rezerwy, _) = await FixtureAsync(db);

            // Listopad u uzytkowniczki, co do grosza.
            await AddAsync(db, rezerwy, -20_000);    // odlozone
            await AddAsync(db, rezerwy, -700_000);
            foreach (var wyjete in new[] { 30_000L, 20_000, 20_000, 50_000, 20_000, 100_000 })
                await AddAsync(db, rezerwy, wyjete);

            var koperta = (await Overview(db))!.Envelopes.Single(e => e.CategoryId == rezerwy);

            koperta.Actual.Grosze.Should().Be(-480_000);
            koperta.Outflow.Grosze.Should().Be(720_000);
            koperta.Inflow.Grosze.Should().Be(240_000);
            koperta.BreakdownLabel.Should().Be(
                $"odłożone {new Money(720_000)}, wyjęte {new Money(240_000)}");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task One_direction_only_means_no_breakdown_at_all()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (rezerwy, _) = await FixtureAsync(db);

            await AddAsync(db, rezerwy, -700_000);

            // Rozbicie powtarzaloby wtedy kwote stojaca obok i zasmiecalo ekran
            // otwierany codziennie.
            (await Overview(db))!.Envelopes.Single(e => e.CategoryId == rezerwy)
                .HasBreakdown.Should().BeFalse();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task On_an_ordinary_category_the_same_split_is_spending_and_refunds()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (_, zakupy) = await FixtureAsync(db);

            await AddAsync(db, zakupy, -36_627);
            await AddAsync(db, zakupy, 1_200);

            var koperta = (await Overview(db))!.Envelopes.Single(e => e.CategoryId == zakupy);

            koperta.BreakdownLabel.Should().Be(
                $"wydane {new Money(36_627)}, zwroty {new Money(1_200)}");
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

    private static async Task AddAsync(CastellanDbContext db, CategoryId kategoria, long grosze)
    {
        db.ChangeTracker.Clear();
        var account = await db.Accounts.FirstAsync();
        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(grosze), DateTimeOffset.Now, kategoria));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<(CategoryId rezerwy, CategoryId zakupy)> FixtureAsync(CastellanDbContext db)
    {
        db.Accounts.Add(Account.Create(
            "ING osobisty", AccountKind.Checking, new Money(2_000_000), DateTimeOffset.UtcNow.AddYears(-1)));
        var rezerwy = Category.Create("Rezerwy", CategoryKind.Expense);
        var zakupy = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Categories.AddRange(rezerwy, zakupy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
            new PlanMonthUseCase.Input(
                YearMonth.Current, new Money(2_000_000),
                [
                    new PlanMonthUseCase.EnvelopeInput(rezerwy.Id, new Money(Plan)),
                    new PlanMonthUseCase.EnvelopeInput(zakupy.Id, new Money(400_000)),
                ],
                []));
        db.ChangeTracker.Clear();

        return (rezerwy.Id, zakupy.Id);
    }

    private static async Task<CastellanDbContext> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;
        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_rozbicie_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
