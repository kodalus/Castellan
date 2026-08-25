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
/// Fundusz to koperta nad pieniędzmi, które leżą na jakimś koncie — wpłata na fundusz
/// nie rusza żadnego konta, tylko podbija własne saldo funduszu. Skoro do poduszki
/// finansowej wchodzą salda wszystkich kont, doliczenie do niej jeszcze salda funduszu
/// liczyłoby tę samą złotówkę drugi raz. Te testy pilnują, żeby znacznik „licz do
/// poduszki” nie wrócił tylnymi drzwiami.
/// </summary>
public class FundsDoNotDoubleCountTest
{
    private static async Task<(CastellanDbContext db, GetCushionOverviewUseCase useCase)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        // Saldo startowe równe miesięcznym wydatkom, żeby po jedynej transakcji konto
        // wyszło na zero — inaczej mieszałoby się w asercje o kwotach.
        var account = Account.Create("ING", AccountKind.Checking, new Money(100_000), DateTimeOffset.UtcNow.AddYears(-1));
        var food = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Accounts.Add(account);
        db.Categories.Add(food);
        await db.SaveChangesAsync();

        // Jeden miesiąc wydatków = 1 000 zł, żeby „miesiące” liczyły się wprost.
        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(-100_000), DateTimeOffset.Now.AddDays(-1), food.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var useCase = new GetCushionOverviewUseCase(
            new AssetRepository(db),
            new CategoryRepository(db),
            new TransactionRepository(db),
            new GetAccountsWithBalancesUseCase(new AccountRepository(db), new TransactionRepository(db)));

        return (db, useCase);
    }

    [Fact]
    public async Task Money_earmarked_by_a_fund_is_counted_once_not_twice()
    {
        // Realny układ: 5 000 zł leży na koncie oszczędnościowym, z czego 3 000 zł
        // jest „obiecane” poduszce bezpieczeństwa. Majątek ma pokazać 5 000 zł.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dblcount_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);

            db.Accounts.Add(Account.Create(
                "Oszczednosciowe PKO", AccountKind.Savings, new Money(500_000), DateTimeOffset.UtcNow.AddDays(-2)));

            var cushion = Fund.Create("Poduszka", FundKind.Emergency, new Money(2_000_000), deadline: null);
            cushion.Contribute(new Money(300_000));
            db.Funds.Add(cushion);

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await useCase.ExecuteAsync();

            overview.TotalValue.Grosze.Should().Be(500_000,
                "pieniądze funduszu leżą już na koncie — doliczenie ich dałoby 8 000 zł");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task No_fund_ever_appears_among_cushion_assets()
    {
        // Poduszka bezpieczeństwa jest tu osobno, bo to ona miała kiedyś znacznik
        // ustawiany domyślnie — czyli podwajała kwotę bez żadnej akcji użytkownika.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dblcount_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);

            var emergency = Fund.Create("Poduszka", FundKind.Emergency, new Money(2_000_000), deadline: null);
            emergency.Contribute(new Money(300_000));

            var vacation = Fund.Create("Urlop", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(8));
            vacation.Contribute(new Money(200_000));

            db.Funds.AddRange(emergency, vacation);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await useCase.ExecuteAsync();

            overview.TotalValue.Grosze.Should().Be(0, "konto wyszło na zero, a fundusze nie doliczają się same z siebie");
            overview.Tiers.SelectMany(t => t.Assets).Should().NotContain(a => a.Name.StartsWith("Fundusz:"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Cash_outside_the_app_still_counts_as_an_asset()
    {
        // Kontrola drugiej strony: rezygnacja ze znacznika nie może odciąć pieniędzy,
        // o których aplikacja wie tylko z ręcznego wpisu. Od tego są aktywa.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dblcount_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath);

            db.Assets.Add(Asset.Create("Gotówka w domu", AssetLiquidity.Immediate, new Money(150_000)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = await useCase.ExecuteAsync();

            overview.TotalValue.Grosze.Should().Be(150_000);
            overview.TotalMonths.Should().BeApproximately(1.5, 0.01);
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dblcount_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _) = await SetupAsync(dbPath);

            db.Funds.Add(Fund.Create("Poduszka", FundKind.Emergency, new Money(2_000_000), deadline: null));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var backup = new Castellan.Infrastructure.Services.BackupService(db);
            var exported = await backup.ExportAsync();
            exported.Funds.Should().ContainSingle().Which.Deadline.Should().BeNull();

            await backup.ImportAsync(exported);
            db.ChangeTracker.Clear();

            (await db.Funds.SingleAsync()).Deadline.Should().BeNull();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
