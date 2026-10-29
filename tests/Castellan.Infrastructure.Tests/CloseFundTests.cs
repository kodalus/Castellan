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
/// Zakończenie funduszu: cel się wydarzył (OC zapłacone, urlop wzięty) i koperta nie
/// ma już czego pilnować.
///
/// To nie to samo co usunięcie. Usunięcie odpina transakcje pokryte z funduszu, przez
/// co dawne wydatki wracają do kopert i miesiące sprzed roku nagle pokazują inne kwoty.
/// Zakończenie nie rusza historii — i właśnie dlatego istnieje osobno.
/// </summary>
public class CloseFundTests
{
    private const long NaOc     = 140_000;
    private const long NaWakacje = 50_000;

    [Fact]
    public async Task Closing_moves_the_remainder_onto_another_goal()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, wakacje) = await SetupAsync(dbPath);

            await Close(db).ExecuteAsync(oc, moveRemainderTo: wakacje);

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync(f => f.Id == oc)).Balance.Grosze.Should().Be(0);
            (await db.Funds.SingleAsync(f => f.Id == wakacje)).Balance.Grosze
                .Should().Be(NaWakacje + NaOc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Closing_creates_no_transaction()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, wakacje) = await SetupAsync(dbPath);

            await Close(db).ExecuteAsync(oc, moveRemainderTo: wakacje);

            db.ChangeTracker.Clear();
            // Fundusz jest kopertą nad pieniędzmi, które leżą na koncie. Zamknięcie
            // koperty nie przesuwa ani grosza, więc historia konta nie ma o czym mówić.
            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_closed_fund_leaves_the_active_list_but_stays_among_the_closed()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, _) = await SetupAsync(dbPath);

            await Close(db).ExecuteAsync(oc);
            db.ChangeTracker.Clear();

            var overview = await new GetFundOverviewUseCase(new FundRepository(db)).ExecuteAsync(paydateDay: 10);

            overview.Items.Should().NotContain(i => i.Id == oc);
            overview.Closed.Should().ContainSingle(c => c.Id == oc);
            // Zakończony cel przestaje się dopominać o ratę w sumie odpisów.
            overview.TotalBalance.Grosze.Should().Be(NaWakacje);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Closing_without_a_destination_keeps_the_remainder_on_record()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, _) = await SetupAsync(dbPath);

            await Close(db).ExecuteAsync(oc);

            db.ChangeTracker.Clear();
            // Reszta przestaje być odłożona, ale nie wyparowuje: zakończony fundusz
            // pamięta, ile na nim zostało, więc przywrócenie oddaje dokładnie to samo.
            (await db.Funds.SingleAsync(f => f.Id == oc)).Balance.Grosze.Should().Be(NaOc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Closing_keeps_expenses_paid_from_the_fund_attached_to_it()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, _) = await SetupAsync(dbPath);
            var txId = await PayFromFundAsync(db, oc);

            await Close(db).ExecuteAsync(oc);

            db.ChangeTracker.Clear();
            // To jest cała różnica wobec usunięcia: wydatek zostaje przypisany do
            // funduszu, więc nie wraca do koperty i nie zmienia minionego miesiąca.
            var tx = await db.Transactions.SingleAsync(t => t.Id == txId);
            tx.PaidFromFundId.Should().Be(oc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_instead_of_closing_sends_those_expenses_back_to_the_envelopes()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, _) = await SetupAsync(dbPath);
            var txId = await PayFromFundAsync(db, oc);

            // Kontrola: usunięcie nadal robi to, co robiło. Gdyby oba wyjścia działały
            // tak samo, osobne „zakończ" nie miałoby po co istnieć.
            await new DeleteFundUseCase(
                new FundRepository(db), new TransactionRepository(db), new UnitOfWork(db)).ExecuteAsync(oc);

            db.ChangeTracker.Clear();
            (await db.Transactions.SingleAsync(t => t.Id == txId)).PaidFromFundId.Should().BeNull();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Reopening_brings_the_fund_back_with_its_balance()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, _) = await SetupAsync(dbPath);

            await Close(db).ExecuteAsync(oc);
            db.ChangeTracker.Clear();
            await Close(db).ReopenAsync(oc);
            db.ChangeTracker.Clear();

            var overview = await new GetFundOverviewUseCase(new FundRepository(db)).ExecuteAsync(paydateDay: 10);
            overview.Items.Should().ContainSingle(i => i.Id == oc && i.Balance.Grosze == NaOc);
            overview.Closed.Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_closed_fund_will_not_take_in_someone_elses_remainder()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, oc, wakacje) = await SetupAsync(dbPath);

            await Close(db).ExecuteAsync(wakacje);
            db.ChangeTracker.Clear();

            // Inaczej pieniądze lądują w kopercie, której nie widać na żadnym ekranie.
            var act = async () => await Close(db).ExecuteAsync(oc, moveRemainderTo: wakacje);

            await act.Should().ThrowAsync<InvalidOperationException>();

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync(f => f.Id == oc)).Balance.Grosze.Should().Be(NaOc);
            (await db.Funds.SingleAsync(f => f.Id == oc)).IsArchived.Should().BeFalse();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static CloseFundUseCase Close(CastellanDbContext db) =>
        new(new FundRepository(db), new UnitOfWork(db));

    private static async Task<TransactionId> PayFromFundAsync(CastellanDbContext db, FundId fundId)
    {
        var account = Account.Create("ING", AccountKind.Checking, new Money(500_000), DateTimeOffset.UtcNow.AddYears(-1));
        var category = Category.Create("Ubezpieczenia", CategoryKind.Expense);
        db.Accounts.Add(account);
        db.Categories.Add(category);

        var tx = Transaction.CreateManual(account.Id, new Money(-NaOc), DateTimeOffset.Now, category.Id);
        tx.PayFromFund(fundId);
        db.Transactions.Add(tx);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return tx.Id;
    }

    private static async Task<(CastellanDbContext db, FundId oc, FundId wakacje)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var oc = Fund.Create("Ubezpieczenie OC", FundKind.Insurance, new Money(NaOc), null);
        oc.Contribute(new Money(NaOc));

        var wakacje = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_000_000), null);
        wakacje.Contribute(new Money(NaWakacje));

        db.Funds.AddRange(oc, wakacje);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, oc.Id, wakacje.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_zamkniecie_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
