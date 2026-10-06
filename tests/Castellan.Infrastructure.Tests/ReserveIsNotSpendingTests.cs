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
/// Odłożenie pieniędzy na rezerwę zabiera je z tego, co można w tym miesiącu wydać —
/// i tak ma być, bo leżą na koncie oszczędnościowym, a nie w portfelu. Ale NIE jest to
/// wydatek: nazywanie tego „wydane" i malowanie na czerwono każe się bać czegoś, co
/// jest dokładnie odwrotnością szkody.
///
/// Scenariusz z życia: w planie 1000 zł na rezerwę, odłożone 10 000 zł. Budżet
/// rzeczywiście schodzi pod kreskę, ale z powodu, który nie jest stratą.
/// </summary>
public class ReserveIsNotSpendingTests
{
    private const long Planned = 100_000;      // 1000 zł w planie na rezerwę
    private const long PutAside = 1_000_000;   // 10 000 zł faktycznie odłożone
    private const long Groceries = 20_000;     // 200 zł zwykłego wydatku

    [Fact]
    public async Task Money_put_aside_is_not_counted_as_spent()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, reserve, food, account) = await SetupAsync(dbPath);

            Spend(db, account, reserve, PutAside);
            Spend(db, account, food, Groceries);
            await db.SaveChangesAsync();

            await PlanAsync(db, [(reserve, Planned), (food, 500_000)]);
            var overview = await OverviewAsync(db);

            overview!.TotalSaved.Grosze.Should().Be(PutAside);
            overview.TotalSpent.Grosze.Should().Be(Groceries,
                "odłożone pieniądze nie wyszły z kieszeni — leżą na oszczędnościach");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Money_put_aside_still_lowers_what_is_left_to_spend()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, reserve, food, account) = await SetupAsync(dbPath);

            Spend(db, account, reserve, PutAside);
            Spend(db, account, food, Groceries);
            await db.SaveChangesAsync();

            await PlanAsync(db, [(reserve, Planned), (food, 500_000)]);
            var overview = await OverviewAsync(db);

            // Gdyby odłożone nie zabierały z puli, aplikacja pokazywałaby pieniądze,
            // których nie ma — a to jest dokładnie to kłamstwo, przed którym budżet
            // kopertowy ma chronić.
            overview!.RemainingToSpend.Grosze.Should().Be(600_000 - PutAside - Groceries);
            overview.IsOverspent.Should().BeTrue();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Going_under_only_because_of_saving_is_told_apart_from_overspending()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, reserve, food, account) = await SetupAsync(dbPath);

            Spend(db, account, reserve, PutAside);
            Spend(db, account, food, Groceries);
            await db.SaveChangesAsync();

            await PlanAsync(db, [(reserve, Planned), (food, 500_000)]);
            var overview = await OverviewAsync(db);

            overview!.SavedOverPlan.Grosze.Should().Be(PutAside - Planned);
            overview.IsOverspentBySaving.Should().BeTrue();
            overview.RemainingWithoutExtraSaving.Grosze.Should().Be(600_000 - Planned - Groceries);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Real_overspending_is_not_excused_by_saving()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, reserve, food, account) = await SetupAsync(dbPath);

            // Odłożone dokładnie tyle, ile w planie — a mimo to budżet pod kreską.
            // Tu nie ma czego tłumaczyć i komunikat nie może udawać, że jest.
            Spend(db, account, reserve, Planned);
            Spend(db, account, food, 900_000);
            await db.SaveChangesAsync();

            await PlanAsync(db, [(reserve, Planned), (food, 500_000)]);
            var overview = await OverviewAsync(db);

            overview!.IsOverspent.Should().BeTrue();
            overview.SavedOverPlan.Grosze.Should().Be(0);
            overview.IsOverspentBySaving.Should().BeFalse();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static void Spend(CastellanDbContext db, AccountId account, CategoryId category, long grosze) =>
        db.Transactions.Add(Transaction.CreateManual(
            account, new Money(-grosze), DateTimeOffset.Now, category));

    private static Task PlanAsync(CastellanDbContext db, (CategoryId Category, long Amount)[] envelopes) =>
        new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
            new PlanMonthUseCase.Input(YearMonth.Current, new Money(600_000),
                [.. envelopes.Select(e => new PlanMonthUseCase.EnvelopeInput(e.Category, new Money(e.Amount)))]));

    private static Task<MonthOverview?> OverviewAsync(CastellanDbContext db) =>
        new GetMonthOverviewUseCase(
            new MonthBudgetRepository(db), new CategoryRepository(db), new TransactionRepository(db))
            .ExecuteAsync(YearMonth.Current);

    private static async Task<(CastellanDbContext db, CategoryId reserve, CategoryId food, AccountId account)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var reserve = Category.Create(ConfirmTransferUseCase.ReserveCategoryName, CategoryKind.Expense);
        var food = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Categories.AddRange(reserve, food);

        var account = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1));
        db.Accounts.Add(account);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, reserve.Id, food.Id, account.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_rezerwa_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
