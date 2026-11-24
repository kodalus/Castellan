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
/// Saldo funduszu jest osobną, zapisaną liczbą — nie wynika z transakcji. Dopóki
/// kasowanie transakcji go nie dotykało, usunięty przelew zostawiał fundusz podniesiony
/// o kwotę, której już nigdzie nie było.
///
/// Scenariusz z życia: przelew 7 000 zł na poduszkę wpisany ręcznie, bo nie przyszło
/// powiadomienie. Powiadomienie przyszło chwilę później i zrobiło drugi przelew.
/// Usunięcie tego drugiego z obu stron zabrało transakcje, ale 7 000 zł zostało
/// w funduszu — poduszka pokazywała 20 000 zł zamiast 13 000 zł.
/// </summary>
public class DeleteRestoresFundTests
{
    private const long Start   = 600_000;    // 6 000 zł już odłożone
    private const long Przelew = 700_000;    // 7 000 zł

    [Fact]
    public async Task Deleting_a_contributing_transfer_takes_the_money_back_out_of_the_fund()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (zKonta, naKonto, poduszka) = await FixtureAsync(db);

            await PrzelewAsync(db, zKonta, naKonto, poduszka, odkladanie: true);
            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start + Przelew);

            var noga = await db.Transactions.FirstAsync(t => t.AccountId == zKonta);
            await UsunAsync(db, noga.Id);

            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start);
            (await db.Transactions.CountAsync()).Should().Be(0, "przelew ginie parami");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task It_does_not_matter_which_leg_of_the_transfer_is_deleted()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (zKonta, naKonto, poduszka) = await FixtureAsync(db);

            await PrzelewAsync(db, zKonta, naKonto, poduszka, odkladanie: true);

            // Powiązanie z funduszem ma tylko JEDNA noga. Gdyby cofanie liczyło się po
            // każdej, usunięcie przelewu zabrałoby funduszowi dwa razy po 7 000 zł.
            var druga = await db.Transactions.FirstAsync(t => t.AccountId == naKonto);
            await UsunAsync(db, druga.Id);

            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_a_withdrawal_gives_the_money_back_to_the_fund()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (zKonta, naKonto, poduszka) = await FixtureAsync(db);

            // Kierunek odwrotny: pieniądze wracają z oszczędności do wydawania.
            await PrzelewAsync(db, naKonto, zKonta, poduszka, odkladanie: false);
            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start - Przelew);

            var noga = await db.Transactions.FirstAsync();
            await UsunAsync(db, noga.Id);

            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_an_expense_paid_from_a_fund_puts_the_money_back()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (konto, _, poduszka) = await FixtureAsync(db);

            var kategoria = Category.Create("Elektronika", CategoryKind.Expense);
            db.Categories.Add(kategoria);
            var wydatek = Transaction.CreateManual(konto, new Money(-120_000), DateTimeOffset.Now, kategoria.Id);
            db.Transactions.Add(wydatek);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await new PayTransactionFromFundUseCase(
                new TransactionRepository(db), new FundRepository(db), new UnitOfWork(db))
                .ExecuteAsync(wydatek.Id, poduszka);
            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start - 120_000);

            await UsunAsync(db, wydatek.Id);

            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start,
                "fundusz nie może zostać uszczuplony o zakup, którego już nie ma");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_plain_transfer_leaves_every_fund_alone()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var (zKonta, naKonto, poduszka) = await FixtureAsync(db);

            db.ChangeTracker.Clear();
            await new CreateTransferUseCase(
                new AccountRepository(db), new TransactionRepository(db),
                new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db))
                .ExecuteAsync(new CreateTransferUseCase.Input(
                    zKonta, naKonto, new Money(Przelew), DateTimeOffset.Now, "zwykle przelozenie"));
            db.ChangeTracker.Clear();

            await UsunAsync(db, (await db.Transactions.FirstAsync()).Id);

            (await SaldoFunduszuAsync(db, poduszka)).Grosze.Should().Be(Start);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task PrzelewAsync(
        CastellanDbContext db, AccountId z, AccountId na, FundId fundusz, bool odkladanie)
    {
        db.ChangeTracker.Clear();
        await new CreateTransferUseCase(
            new AccountRepository(db), new TransactionRepository(db),
            new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db))
            .ExecuteAsync(new CreateTransferUseCase.Input(
                z, na, new Money(Przelew), DateTimeOffset.Now, "poduszka",
                IsReserve: odkladanie,
                ContributeTo: odkladanie ? fundusz : null,
                WithdrawFrom: odkladanie ? null : fundusz,
                IsReserveReturn: !odkladanie));
        db.ChangeTracker.Clear();
    }

    private static async Task UsunAsync(CastellanDbContext db, TransactionId id)
    {
        db.ChangeTracker.Clear();
        await new DeleteTransactionUseCase(
            new TransactionRepository(db), new FundRepository(db), new UnitOfWork(db))
            .ExecuteAsync(id);
        db.ChangeTracker.Clear();
    }

    private static async Task<Money> SaldoFunduszuAsync(CastellanDbContext db, FundId id)
    {
        db.ChangeTracker.Clear();
        return (await db.Funds.FirstAsync(f => f.Id == id)).Balance;
    }

    private static async Task<(AccountId zKonta, AccountId naKonto, FundId poduszka)> FixtureAsync(
        CastellanDbContext db)
    {
        var ing = Account.Create(
            "ING osobisty", AccountKind.Checking, new Money(2_000_000), DateTimeOffset.UtcNow.AddYears(-1));
        var oko = Account.Create(
            "OKO ING", AccountKind.Savings, new Money(600_000), DateTimeOffset.UtcNow.AddYears(-1));
        var poduszka = Fund.Create("Poduszka bezpieczenstwa", FundKind.Emergency, new Money(3_000_000), null);
        poduszka.Contribute(new Money(Start));
        db.Accounts.AddRange(ing, oko);
        db.Funds.Add(poduszka);
        db.Categories.Add(Category.Create("Rezerwy", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (ing.Id, oko.Id, poduszka.Id);
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
        Path.Combine(Path.GetTempPath(), $"castellan_fundusz_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
