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
/// Wydatek w kategorii „Rezerwy" mówi, ile UBYŁO z konta, ale nie mówi, gdzie te
/// pieniądze poszły. Sam w sobie jest więc niepełny: saldo konta źródłowego maleje,
/// docelowego nie rośnie, a suma majątku spada — mimo że pieniądze nadal są.
///
/// Odłożenie na własne konto to przesunięcie, nie wydanie. Jedyne, co ma się zmienić,
/// to koperta „Rezerwy" — bo tych pieniędzy nie da się już w tym miesiącu wydać.
/// </summary>
public class ReserveDestinationTests
{
    private sealed record Env(
        CastellanDbContext Db,
        RecordReserveTransferUseCase Record,
        GetAccountsWithBalancesUseCase Balances,
        GetMonthOverviewUseCase Overview,
        Account Checking,
        Account Savings,
        Category Reserve);

    private static async Task<Env> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        var checking = Account.Create("ING", AccountKind.Checking, new Money(300_000), now, Banks.Ing);
        var savings = Account.Create("OKO ING", AccountKind.Savings, new Money(100_000), now, Banks.Ing);
        var reserve = Category.Create("Rezerwy", CategoryKind.Expense);
        db.Accounts.AddRange(checking, savings);
        db.Categories.Add(reserve);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var budget = MonthBudget.Create(new YearMonth(today.Year, today.Month), new Money(300_000));
        budget.Plan(reserve.Id, new Money(80_000));
        db.MonthBudgets.Add(budget);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var txRepo = new TransactionRepository(db);
        var accountRepo = new AccountRepository(db);

        return new Env(
            db,
            new RecordReserveTransferUseCase(txRepo, accountRepo, new UnitOfWork(db)),
            new GetAccountsWithBalancesUseCase(accountRepo, txRepo),
            new GetMonthOverviewUseCase(new MonthBudgetRepository(db), new CategoryRepository(db), txRepo),
            checking, savings, reserve);
    }

    private static async Task<Transaction> SetAsideAsync(Env env, long grosze)
    {
        var expense = Transaction.CreateManual(
            env.Checking.Id, new Money(-grosze), DateTimeOffset.Now, env.Reserve.Id, "Odłożone");
        env.Db.Transactions.Add(expense);
        await env.Db.SaveChangesAsync();
        env.Db.ChangeTracker.Clear();
        return expense;
    }

    private static async Task<long> TotalOnAccountsAsync(Env env) =>
        (await env.Balances.ExecuteAsync()).Sum(a => a.CurrentBalance.Grosze);

    private static async Task<long> ReserveSpentAsync(Env env)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var overview = await env.Overview.ExecuteAsync(new YearMonth(today.Year, today.Month));
        return overview!.Envelopes.Single(e => e.CategoryName == "Rezerwy").Actual.Grosze;
    }

    [Fact]
    public async Task Money_set_aside_onto_your_own_account_does_not_leave_your_pocket()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_resdest_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var before = await TotalOnAccountsAsync(env);
            before.Should().Be(400_000);

            var expense = await SetAsideAsync(env, 50_000);

            // Sam wydatek to dopiero polowa zdarzenia — tu pieniadze jeszcze „znikaja".
            (await TotalOnAccountsAsync(env)).Should().Be(350_000);

            await env.Record.ExecuteAsync(expense.Id, env.Savings.Id);
            env.Db.ChangeTracker.Clear();

            (await TotalOnAccountsAsync(env)).Should().Be(before,
                "odłożenie to przesunięcie między własnymi kontami, nie wydanie");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_money_lands_on_the_account_you_pointed_at()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_resdest_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var expense = await SetAsideAsync(env, 50_000);

            await env.Record.ExecuteAsync(expense.Id, env.Savings.Id);
            env.Db.ChangeTracker.Clear();

            var balances = (await env.Balances.ExecuteAsync()).ToDictionary(a => a.Name, a => a.CurrentBalance.Grosze);
            balances["ING"].Should().Be(250_000);
            balances["OKO ING"].Should().Be(150_000);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_reserve_envelope_is_charged_once_not_twice()
    {
        // Noga wchodzaca musi zostac wykluczona z budzetu. Gdyby weszla jako przychod
        // albo — gorzej — jako druga strona w „Rezerwach", koperta rozjechalaby sie.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_resdest_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var expense = await SetAsideAsync(env, 50_000);

            (await ReserveSpentAsync(env)).Should().Be(-50_000);

            await env.Record.ExecuteAsync(expense.Id, env.Savings.Id);
            env.Db.ChangeTracker.Clear();

            (await ReserveSpentAsync(env)).Should().Be(-50_000, "dopisanie drugiej nogi nie obciąża koperty ponownie");

            var incoming = await env.Db.Transactions.SingleAsync(t => t.AccountId == env.Savings.Id);
            incoming.IsExcludedFromCalculations.Should().BeTrue();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Pointing_at_the_same_account_changes_nothing()
    {
        // Wpis znoszacy sam siebie byłby gorszy niż brak wpisu.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_resdest_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var expense = await SetAsideAsync(env, 50_000);

            await env.Record.ExecuteAsync(expense.Id, env.Checking.Id);
            env.Db.ChangeTracker.Clear();

            (await env.Db.Transactions.CountAsync()).Should().Be(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_transaction_that_is_gone_is_not_an_error()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_resdest_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);

            var act = async () => await env.Record.ExecuteAsync(TransactionId.New(), env.Savings.Id);
            await act.Should().NotThrowAsync();
            (await env.Db.Transactions.CountAsync()).Should().Be(0);
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
