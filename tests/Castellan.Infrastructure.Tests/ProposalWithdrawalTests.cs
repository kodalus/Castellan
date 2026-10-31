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
/// Przelew z oszczędności na rachunek bieżący to wyjęcie odłożonych pieniędzy — i musi
/// dać się tak oznaczyć NIEZALEŻNIE od tego, czy wpisano go ręcznie, czy złapały go
/// powiadomienia. To jest to samo zdarzenie w banku.
///
/// Ręczny formularz pytał o to od dawna; propozycja ze Skrzynki pytała wyłącznie
/// o kierunek przeciwny („odkładasz na rezerwę?”), więc przy wyjmowaniu jedyną drogą
/// było ręczne poprawianie salda funduszu.
///
/// Wyjęcie jest odwrotnością odkładania i dotyka tej samej koperty: noga wracająca na
/// konto do wydawania zostaje wpisem w „Rezerwach” z kwotą dodatnią.
/// </summary>
public class ProposalWithdrawalTests
{
    private const long FundBalance = 300_000;
    private const long Moved = 120_000;

    [Fact]
    public async Task A_proposal_leaving_savings_is_recognised_as_such()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, _) = await SetupAsync(dbPath);
            await ProposeAsync(db, from: savings, to: checking);

            var proposal = (await Proposals(db).ExecuteAsync()).Single();

            proposal.LeavesSavings.Should().BeTrue();
            proposal.ToIsSavings.Should().BeFalse("to jest kierunek odwrotny do odkładania");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Putting_money_aside_is_not_mistaken_for_taking_it_out()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, _) = await SetupAsync(dbPath);
            await ProposeAsync(db, from: checking, to: savings);

            var proposal = (await Proposals(db).ExecuteAsync()).Single();

            // Gdyby oba pytania mogły paść naraz, użytkownik dostałby dwa sprzeczne
            // okienka o tym samym przelewie.
            proposal.ToIsSavings.Should().BeTrue();
            proposal.LeavesSavings.Should().BeFalse();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Confirming_as_a_withdrawal_lowers_the_fund_and_gives_the_money_back_to_the_month()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, fund) = await SetupAsync(dbPath);
            var groupId = await ProposeAsync(db, from: savings, to: checking);

            await Confirm(db).ExecuteWithWithdrawalAsync(groupId, fund);

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(FundBalance - Moved);

            // Ten test wymagał wcześniej, żeby OBIE nogi wypadały z budżetu — w obawie
            // przed policzeniem odpisu drugi raz. To było pomylenie dwóch rzeczy: odpisu
            // z MINIONEGO miesiąca nie wolno cofać wstecz, ale pieniądze wracają do
            // wydawania w miesiącu, w którym je wyjęto. Koperta „Rezerwy” pokazywała
            // przez to samą sumę wpłat, a budżet był zaniżony o wszystko, co wróciło.
            var rezerwy = await db.Categories.SingleAsync(
                c => c.Name == ConfirmTransferUseCase.ReserveCategoryName);
            var txs = await db.Transactions.ToListAsync();

            var wracajaca = txs.Single(t => t.AccountId == checking);
            wracajaca.CategoryId.Should().Be(rezerwy.Id);
            wracajaca.Amount.Grosze.Should().Be(Moved);
            wracajaca.IsExcludedFromCalculations.Should().BeFalse();

            // Druga noga wypada z budżetu jak każdy przelew, inaczej ta sama kwota
            // policzyłaby się dwa razy.
            txs.Single(t => t.AccountId == savings).IsExcludedFromCalculations.Should().BeTrue();
            txs.Should().OnlyContain(t => t.ProposedTransferGroupId == null);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Taking_money_out_without_naming_a_fund_still_gives_it_back_to_the_month()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, _) = await SetupAsync(dbPath);
            var groupId = await ProposeAsync(db, from: savings, to: checking);

            // „Wyjmuję, ale bez wskazania celu” — wskazanie funduszu jest dobrowolne,
            // bo część rezerw nie ma przypisanego celu.
            await Confirm(db).ExecuteWithWithdrawalAsync(groupId, withdrawFrom: null);

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(FundBalance);

            var rezerwy = await db.Categories.SingleAsync(
                c => c.Name == ConfirmTransferUseCase.ReserveCategoryName);
            (await db.Transactions.SingleAsync(t => t.AccountId == checking))
                .CategoryId.Should().Be(rezerwy.Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Confirming_as_a_plain_move_leaves_both_the_funds_and_the_budget_alone()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, savings, checking, _) = await SetupAsync(dbPath);
            var groupId = await ProposeAsync(db, from: savings, to: checking);

            // Kontrola negatywna: „przekładam” idące zwykłym potwierdzeniem. Bez tego
            // każdy ruch z oszczędnościowego podnosiłby kwotę do wydania.
            await Confirm(db).ExecuteAsync(groupId);

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(FundBalance);
            (await db.Transactions.ToListAsync()).Should().OnlyContain(t => t.IsExcludedFromCalculations);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task<Guid> ProposeAsync(CastellanDbContext db, AccountId from, AccountId to)
    {
        var groupId = Guid.NewGuid();
        var outgoing = Transaction.CreateManual(from, new Money(-Moved), DateTimeOffset.Now, Category.UnsortedId);
        var incoming = Transaction.CreateManual(to, new Money(Moved), DateTimeOffset.Now, Category.UnsortedId);
        outgoing.ProposeTransfer(groupId);
        incoming.ProposeTransfer(groupId);
        db.Transactions.AddRange(outgoing, incoming);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return groupId;
    }

    private static GetTransferProposalsUseCase Proposals(CastellanDbContext db) =>
        new(new TransactionRepository(db), new AccountRepository(db));

    private static ConfirmTransferUseCase Confirm(CastellanDbContext db) =>
        new(new TransactionRepository(db), new CategoryRepository(db),
            new FundRepository(db), new UnitOfWork(db));

    private static async Task<(CastellanDbContext db, AccountId savings, AccountId checking, FundId fund)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var year = DateTimeOffset.UtcNow.AddYears(-1);
        var savings = Account.Create("OKO ING", AccountKind.Savings, new Money(FundBalance), year);
        var checking = Account.Create("ING", AccountKind.Checking, Money.Zero, year);
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
        Path.Combine(Path.GetTempPath(), $"castellan_prop_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
