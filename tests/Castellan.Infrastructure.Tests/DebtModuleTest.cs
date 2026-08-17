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
/// Dług jest lustrzanym odbiciem funduszu: saldo maleje do zera zamiast rosnąć do celu.
/// Kluczowe jest to, że rata ma DWA skutki naraz — obciąża kopertę jako realny wydatek
/// miesiąca i zmniejsza saldo zobowiązania. Rozdzielenie tego groziłoby zrobieniem
/// jednej czynności i zapomnieniem o drugiej.
/// </summary>
public class DebtModuleTest
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
    public async Task Paying_an_installment_records_the_expense_and_shrinks_the_balance()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_debt_{Guid.NewGuid():N}.db");
        try
        {
            var (db, account, category) = await SetupAsync(dbPath);

            var debtRepo = new DebtRepository(db);
            var create = new CreateDebtUseCase(debtRepo, new UnitOfWork(db));
            var debtId = await create.ExecuteAsync(new CreateDebtCommand(
                "Kredyt gotówkowy", DebtKind.CashLoan, new Money(1_200_000), new Money(35_000)));
            db.ChangeTracker.Clear();

            var pay = new PayDebtInstallmentUseCase(
                debtRepo, new AccountRepository(db), new CategoryRepository(db),
                new TransactionRepository(db), new UnitOfWork(db));

            await pay.ExecuteAsync(new PayDebtInstallmentUseCase.Input(
                debtId, account.Id, category.Id, new Money(35_000), DateTimeOffset.UtcNow));

            // Skutek pierwszy: realny wydatek, który obciąży kopertę.
            var tx = await db.Transactions.SingleAsync();
            tx.Amount.Grosze.Should().Be(-35_000);
            tx.CategoryId.Should().Be(category.Id);
            tx.IsExcludedFromCalculations.Should().BeFalse("rata to zwykły wydatek miesiąca");

            // Skutek drugi: saldo zobowiązania zmniejszone.
            var debt = await db.Debts.SingleAsync();
            debt.Balance.Grosze.Should().Be(1_165_000);

            // Saldo konta też się zmieniło.
            var balances = await new GetAccountsWithBalancesUseCase(
                new AccountRepository(db), new TransactionRepository(db)).ExecuteAsync();
            balances.Single(a => a.Id == account.Id).CurrentBalance.Grosze.Should().Be(465_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Overpayment_never_pushes_the_balance_below_zero()
    {
        // Nadpłata ponad pozostały dług to już nie dług — ujemne saldo byłoby
        // bezsensowne i psułoby wartość netto.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_debt_{Guid.NewGuid():N}.db");
        try
        {
            var (db, account, category) = await SetupAsync(dbPath);

            var debtRepo = new DebtRepository(db);
            var debtId = await new CreateDebtUseCase(debtRepo, new UnitOfWork(db)).ExecuteAsync(
                new CreateDebtCommand("Resztówka", DebtKind.Other, new Money(10_000), new Money(5_000)));
            db.ChangeTracker.Clear();

            var pay = new PayDebtInstallmentUseCase(
                debtRepo, new AccountRepository(db), new CategoryRepository(db),
                new TransactionRepository(db), new UnitOfWork(db));

            await pay.ExecuteAsync(new PayDebtInstallmentUseCase.Input(
                debtId, account.Id, category.Id, new Money(30_000), DateTimeOffset.UtcNow));

            var debt = await db.Debts.SingleAsync();
            debt.Balance.Grosze.Should().Be(0);
            debt.IsPaidOff.Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Overview_orders_smallest_first_and_sums_balances_and_installments()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_debt_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, _) = await SetupAsync(dbPath);

            db.Debts.AddRange(
                Debt.Create("Hipoteka",  DebtKind.Mortgage, new Money(30_000_000), new Money(180_000)),
                Debt.Create("Gotówkowy", DebtKind.CashLoan, new Money(1_200_000),  new Money(35_000)),
                Debt.Create("Telefon",   DebtKind.Installment, new Money(200_000), new Money(20_000)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await new GetDebtOverviewUseCase(new DebtRepository(db)).ExecuteAsync();

            // Kolejność kuli śnieżnej — najmniejszy dług pierwszy.
            overview.Items.Select(d => d.Name).Should().ContainInOrder("Telefon", "Gotówkowy", "Hipoteka");
            overview.TotalBalance.Grosze.Should().Be(31_400_000);
            overview.TotalMonthlyInstallments.Grosze.Should().Be(235_000);

            var telefon = overview.Items.First();
            telefon.InstallmentsRemaining.Should().Be(10);
            telefon.ProjectedPayoff.Should().NotBeNull();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Debt_without_an_installment_reports_no_payoff_date_instead_of_inventing_one()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_debt_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, _) = await SetupAsync(dbPath);

            // Pożyczka od rodziny bez harmonogramu — udawanie konkretnej daty
            // byłoby zmyślaniem.
            db.Debts.Add(Debt.Create("Od mamy", DebtKind.FromFamily, new Money(500_000), Money.Zero));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await new GetDebtOverviewUseCase(new DebtRepository(db)).ExecuteAsync();

            var row = overview.Items.Single();
            row.InstallmentsRemaining.Should().BeNull();
            row.ProjectedPayoff.Should().BeNull();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Deleting_a_debt_keeps_the_installments_already_paid()
    {
        // Zapłacone raty to realne wydatki, które wydarzyły się w swoich miesiącach —
        // skasowanie ich razem z długiem zafałszowałoby historię budżetu.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_debt_{Guid.NewGuid():N}.db");
        try
        {
            var (db, account, category) = await SetupAsync(dbPath);

            var debtRepo = new DebtRepository(db);
            var debtId = await new CreateDebtUseCase(debtRepo, new UnitOfWork(db)).ExecuteAsync(
                new CreateDebtCommand("Gotówkowy", DebtKind.CashLoan, new Money(100_000), new Money(10_000)));
            db.ChangeTracker.Clear();

            await new PayDebtInstallmentUseCase(
                debtRepo, new AccountRepository(db), new CategoryRepository(db),
                new TransactionRepository(db), new UnitOfWork(db))
                .ExecuteAsync(new PayDebtInstallmentUseCase.Input(
                    debtId, account.Id, category.Id, new Money(10_000), DateTimeOffset.UtcNow));

            await new DeleteDebtUseCase(debtRepo, new UnitOfWork(db)).ExecuteAsync(debtId);

            (await db.Debts.CountAsync()).Should().Be(0);
            (await db.Transactions.CountAsync()).Should().Be(1, "historia wydatków musi zostać nietknięta");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
