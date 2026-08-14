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
/// Edycja transakcji musi się realnie przełożyć na saldo konta — to był cały sens
/// prośby użytkownika ("transakcje powinny być edytowalne"). Testuje kwotę,
/// kategorię i przeniesienie na inne konto na prawdziwej (migrowanej) bazie.
/// </summary>
public class UpdateTransactionTest
{
    [Fact]
    public async Task Editing_amount_changes_the_account_balance()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_edit_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new CastellanDbContext(options);
            db.Database.Migrate();

            var account = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
            var category = Category.Create("Testowa", CategoryKind.Expense);
            var tx = Transaction.CreateManual(account.Id, new Money(-1000), DateTimeOffset.UtcNow, category.Id);

            db.Accounts.Add(account);
            db.Categories.Add(category);
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();

            var accountRepo = new AccountRepository(db);
            var categoryRepo = new CategoryRepository(db);
            var transactionRepo = new TransactionRepository(db);
            var uow = new UnitOfWork(db);

            var update = new UpdateTransactionUseCase(accountRepo, categoryRepo, transactionRepo, uow);
            await update.ExecuteAsync(new UpdateTransactionUseCase.Input(
                tx.Id, account.Id, new Money(-5000), DateTimeOffset.UtcNow, category.Id, "poprawiona kwota"));

            var getBalances = new GetAccountsWithBalancesUseCase(accountRepo, transactionRepo);
            var balances = await getBalances.ExecuteAsync();

            balances.Single(a => a.Id == account.Id).CurrentBalance.Grosze.Should().Be(-5000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Moving_a_transaction_to_another_account_updates_both_balances()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_edit_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new CastellanDbContext(options);
            db.Database.Migrate();

            var ing = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
            var revolut = Account.Create("Revolut", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
            var category = Category.Create("Testowa", CategoryKind.Expense);
            // Notification-parsed transaction accidentally landed on the wrong account.
            var tx = Transaction.CreateManual(ing.Id, new Money(-2500), DateTimeOffset.UtcNow, category.Id);

            db.Accounts.AddRange(ing, revolut);
            db.Categories.Add(category);
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();

            var accountRepo = new AccountRepository(db);
            var categoryRepo = new CategoryRepository(db);
            var transactionRepo = new TransactionRepository(db);
            var uow = new UnitOfWork(db);

            var update = new UpdateTransactionUseCase(accountRepo, categoryRepo, transactionRepo, uow);
            await update.ExecuteAsync(new UpdateTransactionUseCase.Input(
                tx.Id, revolut.Id, new Money(-2500), DateTimeOffset.UtcNow, category.Id, null));

            var getBalances = new GetAccountsWithBalancesUseCase(accountRepo, transactionRepo);
            var balances = await getBalances.ExecuteAsync();

            balances.Single(a => a.Id == ing.Id).CurrentBalance.Grosze.Should().Be(0);
            balances.Single(a => a.Id == revolut.Id).CurrentBalance.Grosze.Should().Be(-2500);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
