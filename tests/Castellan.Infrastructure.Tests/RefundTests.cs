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
/// Zwrot kosztów: pieniądze wracające za coś już kupionego (reklamacja, oddany towar).
/// Ma pomniejszać kategorię, z której poszła pierwotna płatność, a nie zawyżać przychody.
///
/// Rozpoznajemy go po samej transakcji — kwota dodatnia w kategorii wydatkowej — więc
/// wybór kategorii jest jednocześnie odpowiedzią na pytanie „z czego ten zwrot".
/// </summary>
public class RefundTests
{
    private const long Spent = 30_000;   // 300 zł wydane
    private const long Refund = 10_000;  // 100 zł zwrócone

    [Fact]
    public async Task A_refund_reduces_the_envelope_of_its_own_category()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (restaurants, _, account) = Fixtures(db);
            var month = YearMonth.Current;

            AddTx(db, account, -Spent, restaurants);
            AddTx(db, account, Refund, restaurants);
            await db.SaveChangesAsync();

            await PlanAsync(db, month, restaurants, planned: 50_000);

            var overview = await OverviewAsync(db, month);
            var envelope = overview!.Envelopes.Single(e => e.CategoryId == restaurants);

            envelope.Actual.Grosze.Should().Be(-(Spent - Refund));
            envelope.Remaining.Grosze.Should().Be(50_000 - (Spent - Refund));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refund_is_not_counted_as_income()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (restaurants, salary, account) = Fixtures(db);
            var month = YearMonth.Current;

            AddTx(db, account, -Spent, restaurants);
            AddTx(db, account, Refund, restaurants);
            AddTx(db, account, 500_000, salary);
            await db.SaveChangesAsync();

            var stats = await StatsAsync(db, month);
            var thisMonth = stats.Months.Last();

            // Ta sama kwota nie może jednocześnie pomniejszać koperty w Planie
            // i rosnąć jako przychód w Statystykach.
            thisMonth.Income.Grosze.Should().Be(500_000);
            thisMonth.Expense.Grosze.Should().Be(Spent - Refund);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refund_reduces_the_category_in_the_top_spending_list()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (restaurants, _, account) = Fixtures(db);
            var month = YearMonth.Current;

            AddTx(db, account, -Spent, restaurants);
            AddTx(db, account, Refund, restaurants);
            await db.SaveChangesAsync();

            var stats = await StatsAsync(db, month);

            stats.TopExpenseCategories.Single(c => c.CategoryName == "Restauracje")
                .TotalSpent.Grosze.Should().Be(Spent - Refund);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task An_unsorted_incoming_payment_is_still_income()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (_, _, account) = Fixtures(db);
            var month = YearMonth.Current;

            // „Nieprzypisane" jest kategorią systemową rodzaju wydatkowego, więc wpada
            // pod regułę zwrotu, jeśli się jej nie wyłączy. A to brak decyzji, nie
            // decyzja: wypłata, której nikt jeszcze nie posortował, zniknęłaby
            // z przychodów, zanim ktokolwiek zdążyłby ją zobaczyć.
            AddTx(db, account, 500_000, Category.UnsortedId);
            await db.SaveChangesAsync();

            var stats = await StatsAsync(db, month);

            stats.Months.Last().Income.Grosze.Should().Be(500_000);
            stats.Months.Last().Expense.Grosze.Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refund_lowers_the_average_expense_behind_the_cushion()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (restaurants, _, account) = Fixtures(db);

            AddTx(db, account, -Spent, restaurants);
            AddTx(db, account, Refund, restaurants);
            await db.SaveChangesAsync();

            // Oddany towar nie jest kosztem utrzymania — tyle naprawdę kosztowało życie.
            var cushion = await new GetCushionOverviewUseCase(
                new AssetRepository(db),
                new CategoryRepository(db),
                new TransactionRepository(db),
                new GetAccountsWithBalancesUseCase(new AccountRepository(db), new TransactionRepository(db)))
                .ExecuteAsync(expenseMonths: 1);

            cushion.AvgMonthlyExpense.Grosze.Should().Be(Spent - Refund);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Refunds_larger_than_the_spending_do_not_come_back_as_spending()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (restaurants, _, account) = Fixtures(db);
            var month = YearMonth.Current;

            AddTx(db, account, -Refund, restaurants);
            AddTx(db, account, Spent, restaurants);
            await db.SaveChangesAsync();

            await PlanAsync(db, month, restaurants, planned: 50_000);

            var overview = await OverviewAsync(db, month);

            // Wartość bezwzględna zamieniłaby nadwyżkę zwrotów z powrotem w wydatek.
            overview!.TotalSpent.Grosze.Should().Be(-(Spent - Refund));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (CategoryId restaurants, CategoryId salary, AccountId account) Fixtures(CastellanDbContext db)
    {
        var restaurants = Category.Create("Restauracje", CategoryKind.Expense);
        var salary = Category.Create("Wynagrodzenie", CategoryKind.Income);
        db.Categories.AddRange(restaurants, salary);

        var account = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1));
        db.Accounts.Add(account);
        db.SaveChanges();

        return (restaurants.Id, salary.Id, account.Id);
    }

    private static void AddTx(CastellanDbContext db, AccountId account, long grosze, CategoryId category) =>
        db.Transactions.Add(Transaction.CreateManual(
            account, new Money(grosze), DateTimeOffset.Now, category));

    private static async Task PlanAsync(CastellanDbContext db, YearMonth month, CategoryId category, long planned) =>
        await new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
            new PlanMonthUseCase.Input(month, new Money(100_000),
                [new PlanMonthUseCase.EnvelopeInput(category, new Money(planned))]));

    private static Task<MonthOverview?> OverviewAsync(CastellanDbContext db, YearMonth month) =>
        new GetMonthOverviewUseCase(
            new MonthBudgetRepository(db), new CategoryRepository(db), new TransactionRepository(db))
            .ExecuteAsync(month);

    private static Task<MonthlyStats> StatsAsync(CastellanDbContext db, YearMonth month) =>
        new GetMonthlyStatsUseCase(new TransactionRepository(db), new CategoryRepository(db))
            .ExecuteAsync(month, monthCount: 1);

    private static async Task<CastellanDbContext> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_refund_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
