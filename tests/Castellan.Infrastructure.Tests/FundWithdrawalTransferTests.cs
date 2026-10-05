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
/// Przelew z konta oszczędnościowego na rozliczeniowe to kierunek odwrotny do odkładania:
/// pieniądze wracają do wydania. Jeśli były odkładane na konkretny cel, odkładanie
/// właśnie się kończy i saldo funduszu musi zmaleć — inaczej fundusz pokazywałby
/// zebrane pieniądze, których już na nim nie ma.
///
/// Same nogi przelewu zostają wykluczone z budżetu jak każdy przelew własny: odpis
/// obciążył kopertę „Rezerwy" w miesiącu, w którym pieniądze odkładano, więc powrót
/// nie może ani zmniejszyć, ani zwiększyć budżetu tego miesiąca.
/// </summary>
public class FundWithdrawalTransferTests
{
    private const long FundBalance = 300_000;   // 3000 zł odłożone
    private const long Withdrawn = 120_000;     // 1200 zł wyjęte

    [Fact]
    public async Task Taking_money_out_of_a_fund_lowers_its_balance()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, fund) = await SetupAsync(dbPath);

            await TransferAsync(db, savings, checking, Withdrawn, withdrawFrom: fund);

            db.ChangeTracker.Clear();
            var reloaded = await db.Funds.SingleAsync(f => f.Id == fund);
            reloaded.Balance.Grosze.Should().Be(FundBalance - Withdrawn);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Taking_money_out_of_a_fund_does_not_touch_the_month_budget()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, fund) = await SetupAsync(dbPath);

            await TransferAsync(db, savings, checking, Withdrawn, withdrawFrom: fund);

            // Te pieniądze obciążyły budżet, gdy je odkładano. Policzenie ich teraz
            // jako przychodu zawyżyłoby miesiąc o kwotę, którą już raz rozliczono.
            var txs = await db.Transactions.ToListAsync();
            txs.Should().HaveCount(2);
            txs.Should().OnlyContain(t => t.IsExcludedFromCalculations);

            var balances = await new GetAccountsWithBalancesUseCase(
                new AccountRepository(db), new TransactionRepository(db)).ExecuteAsync();
            balances.Single(a => a.Id == savings).CurrentBalance.Grosze.Should().Be(FundBalance - Withdrawn);
            balances.Single(a => a.Id == checking).CurrentBalance.Grosze.Should().Be(Withdrawn);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_plain_move_between_accounts_leaves_every_fund_alone()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, fund) = await SetupAsync(dbPath);

            // Ten sam kierunek, ale użytkownik odpowiedział „przekładam". Bez tego
            // rozróżnienia każde ruszenie oszczędności zjadałoby fundusz.
            await TransferAsync(db, savings, checking, Withdrawn, withdrawFrom: null);

            db.ChangeTracker.Clear();
            var reloaded = await db.Funds.SingleAsync(f => f.Id == fund);
            reloaded.Balance.Grosze.Should().Be(FundBalance);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Putting_money_aside_and_taking_it_back_leaves_the_fund_where_it_started()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, fund) = await SetupAsync(dbPath);

            await ReserveAsync(db, checking, savings, Withdrawn, contributeTo: fund);
            await TransferAsync(db, savings, checking, Withdrawn, withdrawFrom: fund);

            db.ChangeTracker.Clear();
            var reloaded = await db.Funds.SingleAsync(f => f.Id == fund);
            reloaded.Balance.Grosze.Should().Be(FundBalance);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static Task TransferAsync(
        CastellanDbContext db, AccountId from, AccountId to, long grosze, FundId? withdrawFrom) =>
        Create(db).ExecuteAsync(new CreateTransferUseCase.Input(
            from, to, new Money(grosze), DateTimeOffset.Now, Note: null,
            IsReserve: false, ContributeTo: null, WithdrawFrom: withdrawFrom));

    private static Task ReserveAsync(
        CastellanDbContext db, AccountId from, AccountId to, long grosze, FundId contributeTo) =>
        Create(db).ExecuteAsync(new CreateTransferUseCase.Input(
            from, to, new Money(grosze), DateTimeOffset.Now, Note: null,
            IsReserve: true, ContributeTo: contributeTo));

    private static CreateTransferUseCase Create(CastellanDbContext db) =>
        new(new AccountRepository(db), new TransactionRepository(db),
            new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db));

    private static async Task<(CastellanDbContext db, AccountId savings, AccountId checking, FundId fund)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var savings = Account.Create("OKO ING", AccountKind.Savings, new Money(FundBalance), DateTimeOffset.UtcNow.AddYears(-1));
        var checking = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1));
        db.Accounts.AddRange(savings, checking);

        var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_000_000), null);
        fund.Contribute(new Money(FundBalance));
        db.Funds.Add(fund);

        db.Categories.Add(Category.Create(ConfirmTransferUseCase.ReserveCategoryName, CategoryKind.Expense));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, savings.Id, checking.Id, fund.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_withdraw_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
