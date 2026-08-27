using Castellan.Application;
using Castellan.Application.Repositories;
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
/// Ratunek dla przelewu, którego aplikacja nie rozpoznała. Przy przelewie między kontami
/// TEGO SAMEGO banku powiadomienia nie mówią, którego konta dotyczą, więc obie nogi lądują
/// na jednym koncie — jedna z plusem, druga z minusem. Do tej pory jedynym wyjściem było
/// skasowanie obu i wpisanie przelewu ręcznie, czyli wyrzucenie tego, co aplikacja już
/// wiedziała: kwoty, daty i tego, że coś się wydarzyło.
/// </summary>
public class LinkAsTransferTests
{
    private sealed record Env(
        CastellanDbContext Db,
        GetTransferCandidatesUseCase Candidates,
        LinkAsTransferUseCase Link,
        GetMonthOverviewUseCase Overview,
        GetAccountsWithBalancesUseCase Balances,
        ITransactionRepository Transactions,
        IUnitOfWork Uow,
        Account Checking,
        Account Savings,
        Category Food);

    private static async Task<Env> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        var checking = Account.Create("ING", AccountKind.Checking, new Money(300_000), now, Banks.Ing);
        var savings = Account.Create("OKO ING", AccountKind.Savings, new Money(50_000), now, Banks.Ing);
        var food = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Accounts.AddRange(checking, savings);
        db.Categories.Add(food);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var budget = MonthBudget.Create(new YearMonth(today.Year, today.Month), new Money(300_000));
        budget.Plan(food.Id, new Money(100_000));
        db.MonthBudgets.Add(budget);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var txRepo = new TransactionRepository(db);
        var accountRepo = new AccountRepository(db);

        return new Env(
            db,
            new GetTransferCandidatesUseCase(txRepo, accountRepo),
            new LinkAsTransferUseCase(txRepo, accountRepo, new UnitOfWork(db)),
            new GetMonthOverviewUseCase(new MonthBudgetRepository(db), new CategoryRepository(db), txRepo),
            new GetAccountsWithBalancesUseCase(accountRepo, txRepo),
            txRepo,
            new UnitOfWork(db),
            checking, savings, food);
    }

    /// <summary>Dokładnie to, co robią powiadomienia ING: obie nogi na jednym koncie.</summary>
    private static async Task<(Transaction outgoing, Transaction incoming)> BothOnCheckingAsync(
        Env env, long grosze)
    {
        var at = DateTimeOffset.Now;
        var outgoing = Transaction.CreateManual(env.Checking.Id, new Money(-grosze), at, Category.UnsortedId);
        var incoming = Transaction.CreateManual(env.Checking.Id, new Money(grosze), at.AddSeconds(5), Category.UnsortedId);
        env.Db.Transactions.AddRange(outgoing, incoming);
        await env.Db.SaveChangesAsync();
        env.Db.ChangeTracker.Clear();
        return (outgoing, incoming);
    }

    [Fact]
    public async Task The_opposite_entry_shows_up_as_a_candidate_even_on_the_same_account()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var (outgoing, incoming) = await BothOnCheckingAsync(env, 100);

            var candidates = await env.Candidates.ExecuteAsync(outgoing.Id);

            var only = candidates.Should().ContainSingle().Subject;
            only.Id.Should().Be(incoming.Id);
            only.AccountName.Should().Be("ING", "bo tam wylądowała — i o to właśnie chodzi");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Linking_moves_the_incoming_leg_and_takes_both_out_of_the_budget()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var (outgoing, incoming) = await BothOnCheckingAsync(env, 100);

            var result = await env.Link.ExecuteAsync(outgoing.Id, incoming.Id, env.Savings.Id);
            env.Db.ChangeTracker.Clear();

            result.Should().Be(LinkTransferResult.Linked);

            var byName = (await env.Balances.ExecuteAsync()).ToDictionary(a => a.Name, a => a.CurrentBalance.Grosze);
            byName["ING"].Should().Be(299_900);
            byName["OKO ING"].Should().Be(50_100);

            (await env.Db.Transactions.ToListAsync())
                .Should().OnlyContain(t => t.IsExcludedFromCalculations,
                    "przelew między własnymi kontami nie jest ani wydatkiem, ani przychodem");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Linking_two_entries_on_one_account_without_moving_is_refused()
    {
        // Przelew z definicji łączy DWA konta. Para na jednym koncie znosiłaby samą siebie
        // i byłaby gorsza niż brak wpisu.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var (outgoing, incoming) = await BothOnCheckingAsync(env, 100);

            var result = await env.Link.ExecuteAsync(outgoing.Id, incoming.Id);
            env.Db.ChangeTracker.Clear();

            result.Should().Be(LinkTransferResult.SameAccount);
            (await env.Db.Transactions.ToListAsync())
                .Should().OnlyContain(t => !t.IsExcludedFromCalculations, "nic się nie zmieniło");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_refused_link_does_not_leak_the_account_change_into_the_next_save()
    {
        // Sprawdzenia musza isc PRZED zmianami. Gdy odmowa nastepuje PO wywolaniu
        // SetAccount, konto jest juz przestawione na sledzonym obiekcie — niezapisane,
        // ale gotowe pojechac przy najblizszym zapisie czegokolwiek innego.
        //
        // Uklad jest tu dobrany tak, zeby przeniesienie bylo PRAWDZIWA zmiana: noga
        // wchodzaca stoi na oszczednosciowym, a proba przenosi ja na rozliczeniowe —
        // czyli tam, gdzie stoi noga wychodzaca. Wynik to odmowa, ale zmiana konta juz
        // by sie odbyla. Przy przenoszeniu "na to samo konto, na ktorym juz jest" test
        // przechodzilby takze z bledna kolejnoscia, bo nic by sie realnie nie zmienialo.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);

            var at = DateTimeOffset.Now;
            var outgoing = Transaction.CreateManual(env.Checking.Id, new Money(-100), at, Category.UnsortedId);
            var incoming = Transaction.CreateManual(env.Savings.Id, new Money(100), at, Category.UnsortedId);
            env.Db.Transactions.AddRange(outgoing, incoming);
            await env.Db.SaveChangesAsync();
            env.Db.ChangeTracker.Clear();

            var result = await env.Link.ExecuteAsync(outgoing.Id, incoming.Id, env.Checking.Id);
            result.Should().Be(LinkTransferResult.SameAccount);

            // Cokolwiek innego, co konczy sie zapisem.
            await env.Transactions.AddAsync(Transaction.CreateManual(
                env.Savings.Id, new Money(-1_000), DateTimeOffset.Now, env.Food.Id));
            await env.Uow.SaveChangesAsync();
            env.Db.ChangeTracker.Clear();

            (await env.Db.Transactions.SingleAsync(t => t.Id == incoming.Id))
                .AccountId.Should().Be(env.Savings.Id,
                    "odrzucone laczenie nie ma prawa przesunac konta przy okazji cudzego zapisu");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Amounts_that_are_not_exactly_opposite_are_refused()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);

            var at = DateTimeOffset.Now;
            var a = Transaction.CreateManual(env.Checking.Id, new Money(-100), at, Category.UnsortedId);
            var b = Transaction.CreateManual(env.Savings.Id, new Money(99), at, Category.UnsortedId);
            env.Db.Transactions.AddRange(a, b);
            await env.Db.SaveChangesAsync();
            env.Db.ChangeTracker.Clear();

            (await env.Link.ExecuteAsync(a.Id, b.Id)).Should().Be(LinkTransferResult.NotOpposite);
            (await env.Candidates.ExecuteAsync(a.Id)).Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task An_entry_already_in_a_transfer_is_not_offered_and_not_relinked()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var (outgoing, incoming) = await BothOnCheckingAsync(env, 100);

            await env.Link.ExecuteAsync(outgoing.Id, incoming.Id, env.Savings.Id);
            env.Db.ChangeTracker.Clear();

            (await env.Candidates.ExecuteAsync(outgoing.Id)).Should().BeEmpty();
            (await env.Link.ExecuteAsync(outgoing.Id, incoming.Id))
                .Should().Be(LinkTransferResult.AlreadyLinked);
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
