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
/// Realny scenariusz uzytkowniczki: przerzuca 2000 zl z konta wyplaty (ING) na
/// wspolne konto (Revolut), z ktorego oplaca zakupy. Automatyczne parowanie z
/// powiadomien wymaga, zeby dotarly OBA — gdy zabraknie jednego, wplyw zostalby
/// policzony jako przychod i zawyzyl budzet. Reczny przelew musi dawac ten sam
/// efekt co sparowany automatycznie: obie strony wylaczone z kopert i przychodow,
/// ale salda kont zmienione.
/// </summary>
public class ManualTransferTest
{
    private static async Task<(CastellanDbContext db, Account ing, Account revolut)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var ing = Account.Create("ING", AccountKind.Checking, new Money(500_000), DateTimeOffset.UtcNow.AddDays(-2));
        var revolut = Account.Create("Revolut", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-2));
        db.Accounts.AddRange(ing, revolut);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, ing, revolut);
    }

    [Fact]
    public async Task Manual_transfer_moves_money_between_accounts_without_counting_as_income()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_transfer_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ing, revolut) = await SetupAsync(dbPath);

            var accountRepo = new AccountRepository(db);
            var txRepo = new TransactionRepository(db);
            var create = new CreateTransferUseCase(accountRepo, txRepo, new UnitOfWork(db));

            await create.ExecuteAsync(new CreateTransferUseCase.Input(
                ing.Id, revolut.Id, new Money(200_000), DateTimeOffset.UtcNow, "wspolne zakupy"));

            // Obie strony istnieja i sa oznaczone jako jedna para.
            var txs = await db.Transactions.ToListAsync();
            txs.Should().HaveCount(2);
            txs.Select(t => t.TransferGroupId).Distinct().Should().ContainSingle()
                .Which.Should().NotBeNull();
            txs.Should().OnlyContain(t => t.IsExcludedFromCalculations,
                "przelew wlasnych pieniedzy nie jest ani wydatkiem, ani przychodem");

            // Salda kont faktycznie sie przesunely.
            var balances = await new GetAccountsWithBalancesUseCase(accountRepo, txRepo).ExecuteAsync();
            balances.Single(a => a.Id == ing.Id).CurrentBalance.Grosze.Should().Be(300_000);
            balances.Single(a => a.Id == revolut.Id).CurrentBalance.Grosze.Should().Be(200_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Deleting_one_leg_of_a_transfer_removes_both()
    {
        // Skasowanie samej jednej strony zmienialoby saldo jednego konta bez
        // odpowiednika na drugim — czyli cicho gubiloby pieniadze w ewidencji.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_transfer_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ing, revolut) = await SetupAsync(dbPath);

            var txRepo = new TransactionRepository(db);
            var create = new CreateTransferUseCase(new AccountRepository(db), txRepo, new UnitOfWork(db));
            await create.ExecuteAsync(new CreateTransferUseCase.Input(
                ing.Id, revolut.Id, new Money(200_000), DateTimeOffset.UtcNow));

            var oneLeg = await db.Transactions.FirstAsync();
            var delete = new DeleteTransactionUseCase(txRepo, new UnitOfWork(db));
            await delete.ExecuteAsync(oneLeg.Id);

            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Transfer_to_the_same_account_is_rejected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_transfer_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ing, _) = await SetupAsync(dbPath);

            var create = new CreateTransferUseCase(
                new AccountRepository(db), new TransactionRepository(db), new UnitOfWork(db));

            var act = async () => await create.ExecuteAsync(new CreateTransferUseCase.Input(
                ing.Id, ing.Id, new Money(200_000), DateTimeOffset.UtcNow));

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
