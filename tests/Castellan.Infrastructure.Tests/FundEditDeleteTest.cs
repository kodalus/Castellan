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
/// Transactions.PaidFromFundId nie ma klucza obcego do Funds, więc usunięcie funduszu
/// bez odpięcia transakcji zostawiłoby wskaźniki donikąd: takie wydatki nadal byłyby
/// wyłączone z kopert (IsExcludedFromCalculations patrzy tylko, czy pole jest ustawione),
/// ale nazwa funduszu nie dałaby się odczytać — kwota zniknęłaby z budżetu bez powodu.
/// </summary>
public class FundEditDeleteTest
{
    private static async Task<(CastellanDbContext db, Fund fund, Transaction tx)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var account = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
        var category = Category.Create("Ubezpieczenia", CategoryKind.Expense);
        var fund = Fund.Create("OC auto", FundKind.Insurance, new Money(120_000),
            DateOnly.FromDateTime(DateTime.Today).AddMonths(6));
        fund.Contribute(new Money(50_000));

        var tx = Transaction.CreateManual(account.Id, new Money(-30_000), DateTimeOffset.UtcNow, category.Id);
        tx.PayFromFund(fund.Id);

        db.Accounts.Add(account);
        db.Categories.Add(category);
        db.Funds.Add(fund);
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, fund, tx);
    }

    [Fact]
    public async Task Deleting_a_fund_unlinks_its_transactions_so_they_count_again()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_funddel_{Guid.NewGuid():N}.db");
        try
        {
            var (db, fund, tx) = await SetupAsync(dbPath);

            var delete = new DeleteFundUseCase(
                new FundRepository(db), new TransactionRepository(db), new UnitOfWork(db));

            (await delete.CountLinkedTransactionsAsync(fund.Id)).Should().Be(1);

            var unlinked = await delete.ExecuteAsync(fund.Id);

            unlinked.Should().Be(1);
            (await db.Funds.CountAsync()).Should().Be(0);

            var reloaded = await db.Transactions.SingleAsync(t => t.Id == tx.Id);
            reloaded.PaidFromFundId.Should().BeNull("transakcja musi wrócić do kopert, nie zostać z martwym wskaźnikiem");
            reloaded.IsExcludedFromCalculations.Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Editing_a_fund_keeps_its_balance_and_start_month()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_fundedit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, fund, _) = await SetupAsync(dbPath);
            var originalStartMonth = fund.StartMonth;

            var update = new UpdateFundUseCase(new FundRepository(db), new UnitOfWork(db));
            var newDeadline = DateOnly.FromDateTime(DateTime.Today).AddMonths(12);

            await update.ExecuteAsync(new UpdateFundCommand(
                fund.Id, "OC + AC auto", FundKind.Insurance, new Money(200_000), newDeadline));

            var reloaded = await db.Funds.SingleAsync();
            reloaded.Name.Should().Be("OC + AC auto");
            reloaded.TargetAmount.Grosze.Should().Be(200_000);
            reloaded.Deadline.Should().Be(new DateOnly(newDeadline.Year, newDeadline.Month, 1));

            // Saldo i StartMonth to historia — edycja parametrów nie może ich ruszyć,
            // bo StartMonth jest kotwicą wyliczeń "ile powinno być odłożone do teraz".
            reloaded.Balance.Grosze.Should().Be(50_000);
            reloaded.StartMonth.Should().Be(originalStartMonth);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
