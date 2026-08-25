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
/// Aktywo jest liczbą, nie historią — nic się do niego nie odwołuje, więc usuwa się je
/// na dobre, tak jak fundusz i zobowiązanie na tym samym ekranie. Jedyny skutek jest
/// natychmiast widoczny: poduszka finansowa maleje o jego wartość.
/// </summary>
public class DeleteAssetTest
{
    private static async Task<(CastellanDbContext db, GetCushionOverviewUseCase cushion, DeleteAssetUseCase delete)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        // Saldo konta zerowane transakcją, żeby w asercjach zostały same aktywa.
        var account = Account.Create("ING", AccountKind.Checking, new Money(100_000), DateTimeOffset.UtcNow.AddYears(-1));
        var category = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Accounts.Add(account);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(-100_000), DateTimeOffset.Now.AddDays(-1), category.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var cushion = new GetCushionOverviewUseCase(
            new AssetRepository(db),
            new CategoryRepository(db),
            new TransactionRepository(db),
            new GetAccountsWithBalancesUseCase(new AccountRepository(db), new TransactionRepository(db)));

        var delete = new DeleteAssetUseCase(new AssetRepository(db), new UnitOfWork(db));

        return (db, cushion, delete);
    }

    [Fact]
    public async Task Savings_accounts_count_toward_the_cushion()
    {
        // Zgloszone: konta oszczednosciowe nie pojawialy sie w Majatku wcale, bo
        // wyliczenie bralo wylacznie konta rozliczeniowe.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_delasset_{Guid.NewGuid():N}.db");
        try
        {
            var (db, cushion, _) = await SetupAsync(dbPath);

            db.Accounts.Add(Account.Create(
                "Oszczednosciowe PKO", AccountKind.Savings, new Money(450_000), DateTimeOffset.UtcNow.AddDays(-2)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await cushion.ExecuteAsync();

            overview.TotalValue.Grosze.Should().Be(450_000);
            var immediate = overview.Tiers.Single(t => t.Liquidity == AssetLiquidity.Immediate);
            immediate.Assets.Should().Contain(a => a.Name == "Konto: Oszczednosciowe PKO");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Deleting_an_asset_removes_it_and_shrinks_the_cushion()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_delasset_{Guid.NewGuid():N}.db");
        try
        {
            var (db, cushion, delete) = await SetupAsync(dbPath);

            var keep = Asset.Create("Lokata", AssetLiquidity.Fast, new Money(200_000));
            var drop = Asset.Create("Stare auto", AssetLiquidity.Slow, new Money(500_000));
            db.Assets.AddRange(keep, drop);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await cushion.ExecuteAsync()).TotalValue.Grosze.Should().Be(700_000);

            await delete.ExecuteAsync(drop.Id);
            db.ChangeTracker.Clear();

            (await db.Assets.SingleAsync()).Name.Should().Be("Lokata");
            (await cushion.ExecuteAsync()).TotalValue.Grosze.Should().Be(200_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Deleting_an_asset_leaves_transactions_alone()
    {
        // Kontrola: aktywo nie ma zadnych powiazan, wiec usuniecie go nie moze ruszyc
        // niczego poza soba - inaczej niz usuniecie funduszu, ktore odpina transakcje.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_delasset_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, delete) = await SetupAsync(dbPath);

            var asset = Asset.Create("Obligacje", AssetLiquidity.Medium, new Money(300_000));
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var before = await db.Transactions.CountAsync();

            await delete.ExecuteAsync(asset.Id);
            db.ChangeTracker.Clear();

            (await db.Assets.CountAsync()).Should().Be(0);
            (await db.Transactions.CountAsync()).Should().Be(before);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Deleting_an_asset_that_is_already_gone_is_harmless()
    {
        // Dwa szybkie przesuniecia w tym samym wierszu nie moga wywrocic aplikacji.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_delasset_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, delete) = await SetupAsync(dbPath);

            var asset = Asset.Create("Obligacje", AssetLiquidity.Medium, new Money(300_000));
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await delete.ExecuteAsync(asset.Id);
            db.ChangeTracker.Clear();

            var act = async () => await delete.ExecuteAsync(asset.Id);
            await act.Should().NotThrowAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
