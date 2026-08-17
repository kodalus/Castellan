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
/// Powiązanie istniejącego wydatku z kredytem: transakcja została już zapisana
/// (ręcznie albo z powiadomienia), więc wolno tylko obniżyć saldo. Użycie tutaj
/// PayDebtInstallmentUseCase zdublowałoby wydatek — to jest właśnie ta pułapka,
/// przed którą chroni osobny use case.
/// </summary>
public class ApplyDebtPaymentTest
{
    private static async Task<(CastellanDbContext db, Account account, Category category)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var account = Account.Create("ING", AccountKind.Checking, new Money(500_000), DateTimeOffset.UtcNow.AddDays(-2));
        var category = Category.Create("Kredyty i pożyczki", CategoryKind.Expense);
        db.Accounts.Add(account);
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, account, category);
    }

    [Fact]
    public async Task Linking_an_existing_expense_shrinks_the_debt_without_adding_a_second_transaction()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var (db, account, category) = await SetupAsync(dbPath);

            var debt = Debt.Create("Gotówkowy", DebtKind.CashLoan, new Money(600_000), new Money(30_000));
            db.Debts.Add(debt);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // Wydatek zapisany normalną ścieżką — tak jak przy dodaniu ręcznym
            // albo złapaniu z powiadomienia.
            var addTx = new AddManualTransactionUseCase(
                new AccountRepository(db), new CategoryRepository(db),
                new TransactionRepository(db), new UnitOfWork(db));
            await addTx.ExecuteAsync(new AddManualTransactionUseCase.Input(
                account.Id, new Money(-30_000), DateTimeOffset.UtcNow, category.Id));
            db.ChangeTracker.Clear();

            // Dopiero teraz powiązanie z konkretnym kredytem.
            await new ApplyDebtPaymentUseCase(new DebtRepository(db), new UnitOfWork(db))
                .ExecuteAsync(debt.Id, new Money(30_000));

            (await db.Transactions.CountAsync()).Should().Be(1, "wydatek już istniał — nie wolno go zdublować");
            (await db.Debts.SingleAsync()).Balance.Grosze.Should().Be(570_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Debt_payment_still_counts_as_a_normal_expense_in_the_envelope()
    {
        // Rata obciąża budżet miesiąca jak każdy inny wydatek — inaczej plan
        // przestałby się zgadzać z rzeczywistością.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_link_{Guid.NewGuid():N}.db");
        try
        {
            var (db, account, category) = await SetupAsync(dbPath);

            var debt = Debt.Create("Gotówkowy", DebtKind.CashLoan, new Money(600_000), new Money(30_000));
            db.Debts.Add(debt);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var month = YearMonth.Current;
            await new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
                new PlanMonthUseCase.Input(month, new Money(500_000),
                    [new PlanMonthUseCase.EnvelopeInput(category.Id, new Money(30_000))]));
            db.ChangeTracker.Clear();

            await new AddManualTransactionUseCase(
                new AccountRepository(db), new CategoryRepository(db),
                new TransactionRepository(db), new UnitOfWork(db))
                .ExecuteAsync(new AddManualTransactionUseCase.Input(
                    account.Id, new Money(-30_000), DateTimeOffset.Now, category.Id));
            db.ChangeTracker.Clear();

            await new ApplyDebtPaymentUseCase(new DebtRepository(db), new UnitOfWork(db))
                .ExecuteAsync(debt.Id, new Money(30_000));
            db.ChangeTracker.Clear();

            var overview = await new GetMonthOverviewUseCase(
                new MonthBudgetRepository(db), new CategoryRepository(db), new TransactionRepository(db))
                .ExecuteAsync(month);

            var envelope = overview!.Envelopes.Single(e => e.CategoryId == category.Id);
            envelope.Actual.Grosze.Should().Be(-30_000, "rata to realny wydatek miesiąca");
            overview.TotalSpent.Grosze.Should().Be(30_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
