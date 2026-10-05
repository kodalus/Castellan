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
/// Usunięcie konta jest nieodwracalne i kosztuje więcej, niż sugeruje nazwa: transakcje
/// mają kaskadowy klucz obcy do konta, więc znikają razem z nim.
///
/// Osobno trzeba zająć się przelewami. Przelew to para wpisów na DWÓCH kontach, a druga
/// noga leży na koncie, którego użytkownik nie tykał — kaskada jej nie ruszy. Zostawiona
/// sama, dalej byłaby wyłączona z kopert jako „przelew", ale bez pary: saldo drugiego
/// konta zmieniłoby się bez odpowiednika.
/// </summary>
public class DeleteAccountTests
{
    [Fact]
    public async Task An_account_without_history_is_simply_gone()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, delete) = await SetupAsync(dbPath);
            var account = Account.Create("Stare konto", AccountKind.Checking, Money.Zero, Year());
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var impact = await delete.ExecuteAsync(account.Id);

            impact.Total.Should().Be(0);
            db.ChangeTracker.Clear();
            (await db.Accounts.CountAsync()).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_an_account_takes_its_transactions_with_it()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, delete) = await SetupAsync(dbPath);
            var (account, category) = Fixtures(db);

            db.Transactions.Add(Transaction.CreateManual(account, new Money(-5_000), DateTimeOffset.Now, category));
            db.Transactions.Add(Transaction.CreateManual(account, new Money(-7_000), DateTimeOffset.Now, category));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // Ta liczba trafia do pytania przed usunięciem — na niej opiera się decyzja
            // użytkownika, więc musi być prawdziwa PRZED, nie dopiero po.
            var preview = await delete.PreviewAsync(account);
            preview.OwnTransactions.Should().Be(2);

            var impact = await delete.ExecuteAsync(account);

            impact.OwnTransactions.Should().Be(2);
            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_other_leg_of_a_transfer_does_not_survive_on_the_remaining_account()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, delete) = await SetupAsync(dbPath);
            var (doomed, _) = Fixtures(db);

            var kept = Account.Create("Zostaje", AccountKind.Checking, Money.Zero, Year());
            db.Accounts.Add(kept);
            await db.SaveChangesAsync();

            var transfer = new CreateTransferUseCase(
                new AccountRepository(db), new TransactionRepository(db),
                new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db));
            await transfer.ExecuteAsync(new CreateTransferUseCase.Input(
                doomed, kept.Id, new Money(20_000), DateTimeOffset.Now));

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(2, "przelew to para wpisów");

            var preview = await delete.PreviewAsync(doomed);
            preview.PartnerTransactions.Should().Be(1,
                "druga noga leży na koncie, którego użytkownik nie tykał — trzeba go o niej uprzedzić");

            await delete.ExecuteAsync(doomed);

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(0);
            (await db.Accounts.CountAsync()).Should().Be(1, "drugie konto zostaje");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Archiving_keeps_the_history_and_only_hides_the_account()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, _) = await SetupAsync(dbPath);
            var (account, category) = Fixtures(db);

            db.Transactions.Add(Transaction.CreateManual(account, new Money(-5_000), DateTimeOffset.Now, category));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await new ArchiveAccountUseCase(new AccountRepository(db), new UnitOfWork(db))
                .ExecuteAsync(account);

            db.ChangeTracker.Clear();

            // To jest różnica, dla której archiwizacja jest w tym samym menu co usuwanie:
            // konto znika z list wyboru, a historia zostaje nietknięta.
            (await new AccountRepository(db).ListAsync()).Should().BeEmpty();
            (await db.Accounts.CountAsync()).Should().Be(1);
            (await db.Transactions.CountAsync()).Should().Be(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (AccountId account, CategoryId category) Fixtures(CastellanDbContext db)
    {
        var account = Account.Create("Do usunięcia", AccountKind.Checking, Money.Zero, Year());
        db.Accounts.Add(account);

        var category = Category.Create("Zakupy", CategoryKind.Expense);
        db.Categories.Add(category);

        db.SaveChanges();
        db.ChangeTracker.Clear();

        return (account.Id, category.Id);
    }

    private static DateTimeOffset Year() => DateTimeOffset.UtcNow.AddYears(-1);

    private static async Task<(CastellanDbContext db, DeleteAccountUseCase delete)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var delete = new DeleteAccountUseCase(
            new AccountRepository(db), new TransactionRepository(db), new UnitOfWork(db));

        return (db, delete);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_delacc_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
