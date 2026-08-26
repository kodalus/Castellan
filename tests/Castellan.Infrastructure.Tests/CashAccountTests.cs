using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Parsers;
using Castellan.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Gotówka jest kontem, nie wyjątkiem: portfel ma saldo, wychodzą z niego wydatki,
/// a odłożenie pieniędzy do szuflady jest przelewem, nie zniknięciem.
///
/// Osobny typ istnieje z jednego twardego powodu: portfel musi wypaść z awaryjnego
/// wyboru konta przy powiadomieniach. Gdy treść nie mówi, którego konta dotyczy, wybór
/// spada na pierwsze konto rozliczeniowe w kolejności alfabetycznej — a portfel potrafi
/// tam wygrać i przejąć płatność kartą, której nigdy nie widział.
/// </summary>
public class CashAccountTests
{
    private const string IngPackage = "pl.ing.mojeing";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest,
        GetCushionOverviewUseCase cushion, GetAccountsWithBalancesUseCase balances)>
        SetupAsync(string dbPath, bool cashAsCashKind)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);

        // Dwa warunki naraz, oba konieczne, żeby test sprawdzał TYP, a nie coś obok:
        //
        // • nazwa konta bankowego nie zawiera „ING" — inaczej zawężenie po nazwie banku
        //   samo odsiewa portfel i typ nie ma nic do roboty;
        // • portfel wypada w kolejności alfabetycznej PRZED kontem bankowym, więc przy
        //   awaryjnym „pierwsze konto rozliczeniowe" wygrywa, jeśli nic go nie wyklucza.
        db.Accounts.Add(Account.Create("Gotówka",
            cashAsCashKind ? AccountKind.Cash : AccountKind.Checking, new Money(20_000), now));
        db.Accounts.Add(Account.Create("Rachunek osobisty", AccountKind.Checking, new Money(100_000), now));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var txRepo = new TransactionRepository(db);
        var accountRepo = new AccountRepository(db);

        return (db,
            new IngestRawNotificationUseCase(
                new RawNotificationRepository(db), accountRepo, txRepo,
                new CategoryRuleRepository(db), new UnitOfWork(db),
                [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]),
            new GetCushionOverviewUseCase(
                new AssetRepository(db), new CategoryRepository(db), txRepo,
                new GetAccountsWithBalancesUseCase(accountRepo, txRepo)),
            new GetAccountsWithBalancesUseCase(accountRepo, txRepo));
    }

    /// <summary>Powiadomienie, którego treść nie mówi nic o koncie.</summary>
    private static IngestRawNotificationUseCase.Input Anonymous() =>
        new(IngPackage, "Moje ING. Twój Asystent", "44,00 PLN mniej na Twoim koncie", DateTimeOffset.UtcNow);

    private static async Task<string?> AccountOfSingleAsync(CastellanDbContext db)
    {
        var tx = await db.Transactions.SingleOrDefaultAsync();
        if (tx is null) return null;
        return (await db.Accounts.SingleAsync(a => a.Id == tx.AccountId)).Name;
    }

    [Fact]
    public async Task A_wallet_marked_as_cash_never_catches_a_bank_notification()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cash_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, _, _) = await SetupAsync(dbPath, cashAsCashKind: true);

            await ingest.ExecuteAsync(Anonymous());
            db.ChangeTracker.Clear();

            (await AccountOfSingleAsync(db)).Should().Be("Rachunek osobisty",
                "żaden bank nie powiadamia o gotówce, więc portfel nie jest kandydatem");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_same_wallet_left_as_a_checking_account_does_catch_it()
    {
        // Kontrola pokazująca, po co jest osobny typ: bez niego portfel nazwany „Gotówka"
        // wygrywa alfabetycznie i przejmuje płatność, której nigdy nie widział.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cash_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, _, _) = await SetupAsync(dbPath, cashAsCashKind: false);

            await ingest.ExecuteAsync(Anonymous());
            db.ChangeTracker.Clear();

            (await AccountOfSingleAsync(db)).Should().Be("Gotówka");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Cash_counts_toward_the_cushion_like_any_other_account()
    {
        // Pieniądze w portfelu są dostępne natychmiast — bardziej niż cokolwiek innego.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cash_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, cushion, _) = await SetupAsync(dbPath, cashAsCashKind: true);

            var overview = await cushion.ExecuteAsync();

            overview.TotalValue.Grosze.Should().Be(120_000);
            var immediate = overview.Tiers.Single(t => t.Liquidity == AssetLiquidity.Immediate);
            immediate.Assets.Should().Contain(a => a.Name == "Konto: Gotówka");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Spending_cash_lowers_the_wallet_and_leaves_the_bank_alone()
    {
        // Sedno całego pomysłu: dziś wydatek gotówkowy obciążał konto bankowe, którego
        // użytkownik nie ruszał, i zaniżał jego saldo.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_cash_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, _, balances) = await SetupAsync(dbPath, cashAsCashKind: true);

            var wallet = await db.Accounts.SingleAsync(a => a.Name == "Gotówka");
            var food = await db.Categories.SingleAsync(c => c.Name == "Produkty do domu");
            db.Transactions.Add(Transaction.CreateManual(
                wallet.Id, new Money(-3_500), DateTimeOffset.Now, food.Id));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var byName = (await balances.ExecuteAsync()).ToDictionary(a => a.Name, a => a.CurrentBalance.Grosze);
            byName["Gotówka"].Should().Be(16_500);
            byName["Rachunek osobisty"].Should().Be(100_000);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
