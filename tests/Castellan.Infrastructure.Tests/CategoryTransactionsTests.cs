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
/// Rozwinięcie liczby ze Statystyk: które transakcje złożyły się na kwotę w kategorii.
///
/// Najważniejsze jest to, żeby suma pod listą zgadzała się co do grosza z kwotą,
/// w którą użytkownik przed chwilą tapnął. Dwie definicje tego samego — jedna
/// w statystykach, druga na liście — rozjeżdżają się przy pierwszej zmianie
/// w którejkolwiek, a wtedy nie ma jak dojść, która z liczb kłamie.
/// </summary>
public class CategoryTransactionsTests
{
    private const long Lidl      = 31_361;
    private const long Biedronka = 5_266;
    private const long Zwrot     = 1_200;

    [Fact]
    public async Task The_total_matches_the_number_shown_in_statistics()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy) = await SetupAsync(dbPath);

            var zeStatystyk = (await Stats(db)).TopExpenseCategories
                .Single(c => c.CategoryId == zakupy).TotalSpent;
            var zListy = (await Lista(db, zakupy)).Total;

            zListy.Should().Be(zeStatystyk);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refund_lowers_the_total_and_says_so_in_its_row()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy) = await SetupAsync(dbPath);

            var dane = await Lista(db, zakupy);

            dane.Total.Grosze.Should().Be(Lidl + Biedronka - Zwrot);
            dane.Items.Should().ContainSingle(i => i.IsRefund)
                .Which.Amount.Grosze.Should().Be(Zwrot);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Transfers_and_fund_paid_expenses_stay_out()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy) = await SetupAsync(dbPath);

            // Noga przelewu i wydatek pokryty z funduszu nie licza sie do budzetu,
            // wiec nie moga sie tu pojawic — inaczej suma rozjechalaby sie ze Statystykami.
            await AddAsync(db, zakupy, -10_000, transfer: true);
            await AddAsync(db, zakupy, -20_000, zFunduszu: true);

            var dane = await Lista(db, zakupy);

            dane.Items.Should().HaveCount(3);
            dane.Total.Grosze.Should().Be(Lidl + Biedronka - Zwrot);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Only_the_chosen_month_is_listed_when_one_month_is_asked_for()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy) = await SetupAsync(dbPath);
            await AddAsync(db, zakupy, -9_900, kiedy: DateTimeOffset.Now.AddMonths(-2));

            var jedenMiesiac = await Lista(db, zakupy);
            var trzyMiesiace = await Lista(db, zakupy, monthCount: 3);

            jedenMiesiac.Items.Should().HaveCount(3);
            trzyMiesiace.Items.Should().HaveCount(4);
            trzyMiesiace.Total.Grosze.Should().Be(Lidl + Biedronka - Zwrot + 9_900);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Newest_first()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, zakupy) = await SetupAsync(dbPath);

            var items = (await Lista(db, zakupy)).Items;

            items.Should().BeInDescendingOrder(i => i.OccurredAt);
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

    private static Task<CategoryTransactions> Lista(
        CastellanDbContext db, CategoryId kategoria, int monthCount = 1)
    {
        db.ChangeTracker.Clear();
        return new GetCategoryTransactionsUseCase(
            new TransactionRepository(db), new CategoryRepository(db), new AccountRepository(db))
            .ExecuteAsync(kategoria, YearMonth.Current, monthCount);
    }

    private static async Task AddAsync(
        CastellanDbContext db, CategoryId kategoria, long grosze,
        DateTimeOffset? kiedy = null, bool transfer = false, bool zFunduszu = false)
    {
        db.ChangeTracker.Clear();
        var account = await db.Accounts.FirstAsync();
        var tx = Transaction.CreateManual(
            account.Id, new Money(grosze), kiedy ?? DateTimeOffset.Now, kategoria);

        if (transfer) tx.SetTransferGroup(Guid.NewGuid());
        if (zFunduszu)
        {
            var fund = await db.Funds.FirstAsync();
            tx.PayFromFund(fund.Id);
        }

        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<(CastellanDbContext db, CategoryId zakupy)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        db.Accounts.Add(Account.Create(
            "ING osobisty", AccountKind.Checking, new Money(1_000_000), DateTimeOffset.UtcNow.AddYears(-1)));
        var zakupy = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Categories.Add(zakupy);
        db.Funds.Add(Fund.Create("Wakacje", FundKind.Vacation, new Money(1_000_000), null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await AddAsync(db, zakupy.Id, -Lidl,      kiedy: DateTimeOffset.Now.AddDays(-3));
        await AddAsync(db, zakupy.Id, -Biedronka, kiedy: DateTimeOffset.Now.AddDays(-2));
        await AddAsync(db, zakupy.Id, Zwrot,      kiedy: DateTimeOffset.Now.AddDays(-1));

        return (db, zakupy.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_kategoria_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
