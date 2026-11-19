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
/// Stuknięcie w kopertę na pulpicie otwiera listę jej transakcji — ten sam ekran,
/// który od dawna otwierają Statystyki.
///
/// Liczby na pulpicie i w statystykach powstają w DWÓCH różnych miejscach
/// (GetMonthOverviewUseCase i GetCategoryTransactionsUseCase), a teraz prowadzą do
/// tej samej listy. Muszą się zgadzać co do grosza, inaczej nie ma jak dojść, która
/// z nich kłamie — i po to jest ten test.
/// </summary>
public class EnvelopeDrillDownTests
{
    private const long Plan      = 100_000;
    private const long Lidl      = 31_361;
    private const long Biedronka = 5_266;
    private const long Zwrot     = 1_200;
    private const long ZFunduszu = 20_000;

    [Fact]
    public async Task The_list_total_equals_the_number_shown_on_the_envelope()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var zakupy = await FixtureAsync(db);

            var koperta = (await Overview(db))!.Envelopes.Single(e => e.CategoryId == zakupy);
            var lista = await Lista(db, zakupy);

            lista.Total.Grosze.Should().Be(-koperta.Actual.Grosze);
            lista.Total.ToString().Should().Be(koperta.ActualDisplay,
                "to jest dokładnie ta liczba, w którą użytkownik przed chwilą stuknął");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refund_lowers_both_the_envelope_and_the_list_by_the_same_amount()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var zakupy = await FixtureAsync(db);

            (await Overview(db))!.Envelopes.Single(e => e.CategoryId == zakupy)
                .Actual.Grosze.Should().Be(-(Lidl + Biedronka - Zwrot));
            (await Lista(db, zakupy)).Total.Grosze.Should().Be(Lidl + Biedronka - Zwrot);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task What_the_envelope_leaves_out_the_list_leaves_out_too()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var zakupy = await FixtureAsync(db);

            // Wydatek pokryty z funduszu nie obciąża koperty — i nie może pojawić się
            // na liście, bo wtedy suma pod nią byłaby inna niż na pulpicie.
            var lista = await Lista(db, zakupy);

            lista.Items.Should().HaveCount(3);
            lista.Items.Should().NotContain(i => i.Amount.Grosze == -ZFunduszu);
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

    private static Task<CategoryTransactions> Lista(CastellanDbContext db, CategoryId kategoria)
    {
        db.ChangeTracker.Clear();
        return new GetCategoryTransactionsUseCase(
            new TransactionRepository(db), new CategoryRepository(db), new AccountRepository(db))
            .ExecuteAsync(kategoria, YearMonth.Current, monthCount: 1);
    }

    private static async Task<CategoryId> FixtureAsync(CastellanDbContext db)
    {
        var zakupy = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Categories.Add(zakupy);
        db.Funds.Add(Fund.Create("Wakacje", FundKind.Vacation, new Money(1_000_000), null));
        db.Accounts.Add(Account.Create(
            "ING osobisty", AccountKind.Checking, new Money(1_000_000), DateTimeOffset.UtcNow.AddYears(-1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await AddAsync(db, zakupy.Id, -Lidl);
        await AddAsync(db, zakupy.Id, -Biedronka);
        await AddAsync(db, zakupy.Id, Zwrot);
        await AddAsync(db, zakupy.Id, -ZFunduszu, zFunduszu: true);

        db.ChangeTracker.Clear();
        await new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
            new PlanMonthUseCase.Input(
                YearMonth.Current, new Money(1_000_000),
                [new PlanMonthUseCase.EnvelopeInput(zakupy.Id, new Money(Plan))],
                []));
        db.ChangeTracker.Clear();

        return zakupy.Id;
    }

    private static async Task AddAsync(
        CastellanDbContext db, CategoryId kategoria, long grosze, bool zFunduszu = false)
    {
        db.ChangeTracker.Clear();
        var account = await db.Accounts.FirstAsync();
        var tx = Transaction.CreateManual(account.Id, new Money(grosze), DateTimeOffset.Now, kategoria);
        if (zFunduszu) tx.PayFromFund((await db.Funds.FirstAsync()).Id);
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
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
        Path.Combine(Path.GetTempPath(), $"castellan_koperta_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
