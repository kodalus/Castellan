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
/// Do „ile miesięcy wytrzymam" wchodzą tylko fundusze z zaznaczonym znacznikiem
/// rezerwy. Poduszka bezpieczeństwa dostaje go przy zakładaniu, reszta nie — bo
/// tamte pieniądze mają przypisany konkretny przyszły wydatek, więc doliczenie
/// ich zawyżałoby odporność. Znacznik można przestawić ręcznie w obie strony.
/// </summary>
public class EmergencyFundCushionTest
{
    private static async Task<(CastellanDbContext db, GetCushionOverviewUseCase useCase, Category food)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        // Saldo startowe równe miesięcznym wydatkom, żeby po jedynej transakcji konto
        // wyszło na zero. Salda kont rozliczeniowych wchodzą do płynności
        // natychmiastowej, więc inaczej mieszałyby się w asercje o funduszach.
        var account = Account.Create("ING", AccountKind.Checking, new Money(100_000), DateTimeOffset.UtcNow.AddYears(-1));
        var food = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Accounts.Add(account);
        db.Categories.Add(food);
        await db.SaveChangesAsync();

        // Jeden miesiąc wydatków = 1 000 zł, żeby „miesiące" liczyły się wprost.
        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(-100_000), DateTimeOffset.Now.AddDays(-1), food.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var useCase = new GetCushionOverviewUseCase(
            new AssetRepository(db),
            new FundRepository(db),
            new TransactionRepository(db),
            new GetAccountsWithBalancesUseCase(new AccountRepository(db), new TransactionRepository(db)));

        return (db, useCase, food);
    }

    [Fact]
    public async Task Emergency_fund_raises_the_cushion_but_other_funds_do_not()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cushion_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase, _) = await SetupAsync(dbPath);

            var cushion = Fund.Create("Poduszka", FundKind.Emergency, new Money(2_000_000), deadline: null);
            cushion.Contribute(new Money(300_000));

            var vacation = Fund.Create("Urlop", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(8));
            vacation.Contribute(new Money(200_000));

            db.Funds.AddRange(cushion, vacation);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await useCase.ExecuteAsync();

            // Do sumy wchodzi wyłącznie poduszka (3 000 zł), nie urlop (2 000 zł).
            overview.TotalValue.Grosze.Should().Be(300_000);

            var immediate = overview.Tiers.Single(t => t.Liquidity == AssetLiquidity.Immediate);
            immediate.Assets.Should().Contain(a => a.Name == "Fundusz: Poduszka");
            immediate.Assets.Should().NotContain(a => a.Name.Contains("Urlop"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Any_fund_can_be_marked_as_reserve_by_hand()
    {
        // Znacznik nie jest przywiązany do rodzaju: „Wakacje", które realnie są zwykłym
        // oszczędzaniem, wolno wliczyć, a poduszkę wolno wyłączyć.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cushion_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase, _) = await SetupAsync(dbPath);

            var vacation = Fund.Create("Wakacje", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(8));
            vacation.Contribute(new Money(200_000));
            db.Funds.Add(vacation);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await useCase.ExecuteAsync()).TotalValue.Grosze.Should().Be(0);

            await new SetFundCushionFlagUseCase(new FundRepository(db), new UnitOfWork(db))
                .ExecuteAsync(vacation.Id, true);
            db.ChangeTracker.Clear();

            (await useCase.ExecuteAsync()).TotalValue.Grosze.Should().Be(200_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Archived_emergency_fund_stops_counting()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cushion_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase, _) = await SetupAsync(dbPath);

            var cushion = Fund.Create("Poduszka", FundKind.Emergency, new Money(2_000_000), deadline: null);
            cushion.CountsTowardCushion.Should().BeTrue("poduszka dostaje znacznik przy zakładaniu");
            cushion.Contribute(new Money(300_000));
            cushion.Archive();
            db.Funds.Add(cushion);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await useCase.ExecuteAsync()).TotalValue.Grosze.Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Open_ended_fund_survives_a_backup_round_trip()
    {
        // Termin null musi przejść przez eksport i import — inaczej poduszka wróciłaby
        // z datą albo import wywróciłby się na pustej kolumnie.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cushion_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, _) = await SetupAsync(dbPath);

            db.Funds.Add(Fund.Create("Poduszka", FundKind.Emergency, new Money(2_000_000), deadline: null));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var backup = new Castellan.Infrastructure.Services.BackupService(db);
            var exported = await backup.ExportAsync();
            var dto = exported.Funds.Should().ContainSingle().Subject;
            dto.Deadline.Should().BeNull();
            dto.CountsTowardCushion.Should().BeTrue("znacznik musi przetrwać kopię zapasową");

            await backup.ImportAsync(exported);
            db.ChangeTracker.Clear();

            var restored = await db.Funds.SingleAsync();
            restored.Deadline.Should().BeNull();
            restored.CountsTowardCushion.Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
