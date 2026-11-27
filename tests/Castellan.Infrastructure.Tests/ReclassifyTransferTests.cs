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
/// Zmiana rodzaju zapisanego juz przelewu.
///
/// Pytanie „odkladasz czy przekladasz?" padalo raz, przy zapisie, i nie dalo sie go
/// odwolac. Z oszczednosci wyszlo w listopadzie 3 100 zl, a koperta „Rezerwy" liczyla
/// 2 400 zl, bo przy dwoch przelewach (600 i 100 zl) padla odpowiedz „Przekladam".
/// Jedynym ratunkiem bylo skasowanie przelewu i wpisanie go od nowa — czyli operacja,
/// ktora przedtem psula salda funduszy.
/// </summary>
public class ReclassifyTransferTests
{
    private const long Start = 600_000;   // 6 000 zl w funduszu
    private const long Kwota =  60_000;   // 600 zl

    [Fact]
    public async Task A_plain_transfer_becomes_a_withdrawal_and_lands_in_the_reserve_envelope()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var f = await FixtureAsync(db);

            // Tak to wygladalo w danych: 600 zl wyszlo z OKO na ING jako zwykle przelozenie.
            await PlainTransferAsync(db, f.Oszczednosci, f.Biezace);
            (await ReserveEnvelopeAsync(db)).Grosze.Should().Be(0, "zwykly przelew nie dotyka kopert");

            var noga = await db.Transactions.FirstAsync(t => t.AccountId == f.Biezace);
            await ReclassifyAsync(db, noga.Id, TransferClassification.ReserveReturn, f.Poduszka);

            // Kwota DODATNIA w „Rezerwach": pieniadze wrocily do wydania, wiec koperta maleje.
            (await ReserveEnvelopeAsync(db)).Grosze.Should().Be(Kwota);
            (await FundBalanceAsync(db, f.Poduszka)).Grosze.Should().Be(Start - Kwota);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Switching_back_and_forth_leaves_no_trace_in_the_fund()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var f = await FixtureAsync(db);

            await PlainTransferAsync(db, f.Biezace, f.Oszczednosci);
            var noga = await db.Transactions.FirstAsync(t => t.AccountId == f.Biezace);

            await ReclassifyAsync(db, noga.Id, TransferClassification.Reserve, f.Poduszka);
            (await FundBalanceAsync(db, f.Poduszka)).Grosze.Should().Be(Start + Kwota);

            // Najwazniejsze: nowy rodzaj najpierw COFA to, co zrobil poprzedni. Inaczej
            // kazde przelaczenie zostawialoby w funduszu kolejna kwote.
            await ReclassifyAsync(db, noga.Id, TransferClassification.Plain, null);
            (await FundBalanceAsync(db, f.Poduszka)).Grosze.Should().Be(Start);
            (await ReserveEnvelopeAsync(db)).Grosze.Should().Be(0);

            await ReclassifyAsync(db, noga.Id, TransferClassification.Reserve, f.Poduszka);
            (await FundBalanceAsync(db, f.Poduszka)).Grosze.Should().Be(Start + Kwota);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Both_legs_change_together_and_the_account_balances_stay_put()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var f = await FixtureAsync(db);

            await PlainTransferAsync(db, f.Biezace, f.Oszczednosci);
            var przed = await BalancesAsync(db);

            var noga = await db.Transactions.FirstAsync(t => t.AccountId == f.Oszczednosci);
            await ReclassifyAsync(db, noga.Id, TransferClassification.Reserve, null);

            db.ChangeTracker.Clear();
            var nogi = await db.Transactions.ToListAsync();
            nogi.Should().HaveCount(2);
            nogi.Select(t => t.TransferGroupId).Distinct().Should().ContainSingle(
                "obie nogi musza zostac spiete, inaczej skasowanie jednej osieroci druga");

            // Zmiana rodzaju opisuje te same pieniadze inaczej — kwoty sie nie ruszaja.
            (await BalancesAsync(db)).Should().BeEquivalentTo(przed);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_reserve_leg_saved_before_the_legs_were_linked_can_still_be_changed()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var f = await FixtureAsync(db);

            // Dane sprzed powiazania nog: noga w „Rezerwach" nie ma TransferGroupId,
            // wiec pary nie da sie znalezc po grupie. Zostaje slad z momentu zapisu —
            // ta sama chwila i kwota dokladnie przeciwna.
            await LegacyReserveTransferAsync(db, f);
            (await ReserveEnvelopeAsync(db)).Grosze.Should().Be(-Kwota);

            var noga = await db.Transactions.FirstAsync(t => t.AccountId == f.Biezace);
            await ReclassifyAsync(db, noga.Id, TransferClassification.Plain, null);

            (await ReserveEnvelopeAsync(db)).Grosze.Should().Be(0);
            db.ChangeTracker.Clear();
            (await db.Transactions.ToListAsync())
                .Should().OnlyContain(t => t.Kind == TransactionKind.Transfer);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_transaction_without_a_counterpart_is_left_alone()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var f = await FixtureAsync(db);

            var samotna = Transaction.CreateManual(
                f.Biezace, new Money(-Kwota), DateTimeOffset.Now, Category.UnsortedId);
            db.Transactions.Add(samotna);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var act = async () => await ReclassifyAsync(
                db, samotna.Id, TransferClassification.Reserve, f.Poduszka);

            // Zmiana rodzaju jednej nogi rozjechalaby salda kont. Lepiej odmowic.
            await act.Should().ThrowAsync<InvalidOperationException>();
            (await FundBalanceAsync(db, f.Poduszka)).Grosze.Should().Be(Start);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task ReclassifyAsync(
        CastellanDbContext db, TransactionId id, TransferClassification kind, FundId? fund)
    {
        db.ChangeTracker.Clear();
        await new ReclassifyTransferUseCase(
            new TransactionRepository(db), new CategoryRepository(db),
            new FundRepository(db), new UnitOfWork(db))
            .ExecuteAsync(new ReclassifyTransferUseCase.Input(id, kind, fund));
        db.ChangeTracker.Clear();
    }

    private static async Task PlainTransferAsync(CastellanDbContext db, AccountId z, AccountId na)
    {
        db.ChangeTracker.Clear();
        await new CreateTransferUseCase(
            new AccountRepository(db), new TransactionRepository(db),
            new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db))
            .ExecuteAsync(new CreateTransferUseCase.Input(
                z, na, new Money(Kwota), DateTimeOffset.Now, "Przelew wlasny"));
        db.ChangeTracker.Clear();
    }

    /// <summary>Przelew na rezerwe zapisany tak, jak robila to wersja sprzed spiecia nog.</summary>
    private static async Task LegacyReserveTransferAsync(CastellanDbContext db, Fixture f)
    {
        db.ChangeTracker.Clear();
        var kiedy = DateTimeOffset.Now;
        var rezerwy = await db.Categories.FirstAsync(c => c.Name == "Rezerwy");

        var wychodzaca = Transaction.CreateManual(f.Biezace, new Money(-Kwota), kiedy, Category.TransferId);
        wychodzaca.MarkAsReserveMove(rezerwy.Id);          // bez grupy — tak bylo wczesniej
        var przychodzaca = Transaction.CreateManual(f.Oszczednosci, new Money(Kwota), kiedy, Category.TransferId);
        przychodzaca.SetTransferGroup(Guid.NewGuid());

        db.Transactions.AddRange(wychodzaca, przychodzaca);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<Money> ReserveEnvelopeAsync(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        var rezerwy = await db.Categories.FirstAsync(c => c.Name == "Rezerwy");
        var txs = await db.Transactions.ToListAsync();
        return new Money(txs
            .Where(t => t.CategoryId == rezerwy.Id && !t.IsExcludedFromCalculations)
            .Sum(t => t.Amount.Grosze));
    }

    private static async Task<Money> FundBalanceAsync(CastellanDbContext db, FundId id)
    {
        db.ChangeTracker.Clear();
        return (await db.Funds.FirstAsync(f => f.Id == id)).Balance;
    }

    private static async Task<Dictionary<string, long>> BalancesAsync(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        var salda = await new GetAccountsWithBalancesUseCase(
            new AccountRepository(db), new TransactionRepository(db)).ExecuteAsync();
        return salda.ToDictionary(a => a.Name, a => a.CurrentBalance.Grosze);
    }

    private sealed record Fixture(AccountId Biezace, AccountId Oszczednosci, FundId Poduszka);

    private static async Task<Fixture> FixtureAsync(CastellanDbContext db)
    {
        var kiedys = DateTimeOffset.UtcNow.AddYears(-1);
        var ing = Account.Create("ING osobisty", AccountKind.Checking, new Money(1_000_000), kiedys);
        var oko = Account.Create("OKO ING", AccountKind.Savings, new Money(Start), kiedys);
        var poduszka = Fund.Create("Poduszka", FundKind.Emergency, new Money(5_000_000), null);
        poduszka.Contribute(new Money(Start));

        db.Accounts.AddRange(ing, oko);
        db.Funds.Add(poduszka);
        db.Categories.Add(Category.Create("Rezerwy", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new Fixture(ing.Id, oko.Id, poduszka.Id);
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
        Path.Combine(Path.GetTempPath(), $"castellan_rodzaj_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
