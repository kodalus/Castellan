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
/// Dobieranie drugiej nogi przelewu między własnymi kontami.
///
/// Warunkiem jest sama kwota ze znakiem przeciwnym na innym koncie — nic więcej. Przy
/// oknie dwóch dób i wyborze „pierwszy z brzegu" (a repozytorium nie sortuje, więc
/// w praktyce najstarszy) przelew z ING osobistego na OKO ING łączył się z przypadkową
/// wpłatą o tej samej kwocie sprzed godzin na zupełnie inne konto. W rozpoznanym
/// przelewie pojawiał się wtedy Revolut, którego ta operacja w ogóle nie dotyczyła,
/// a prawdziwa druga noga zostawała bez pary.
///
/// Zbieg kwot nie jest tu rzadki: przelewy robi się na równe kwoty.
/// </summary>
public class TransferPairingTests
{
    private const string IngPackage = "pl.ing.mojeing";
    private const long Kwota = 50_000;

    [Fact]
    public async Task The_leg_closest_in_time_wins_over_an_older_coincidence()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, ingest) = await SetupAsync(dbPath);
            var teraz = DateTimeOffset.UtcNow;

            // Przypadkowa wpłata o tej samej kwocie, godzinę wcześniej, na obce konto.
            await AddAsync(db, "Revolut wspólny", Kwota, teraz.AddHours(-1));
            // Prawdziwa druga noga: ta sama kwota, kilka sekund temu.
            await AddAsync(db, "OKO ING", Kwota, teraz.AddSeconds(-5));

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Twój Asystent", "500 PLN mniej na Twoim koncie - Przelew własny", teraz));

            (await PairedWithAsync(db)).Should().BeEquivalentTo(["ING osobisty", "OKO ING"]);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_coincidence_from_two_days_ago_is_not_a_transfer_leg()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, ingest) = await SetupAsync(dbPath);
            var teraz = DateTimeOffset.UtcNow;

            // Sama kwota przeciwna sprzed dwóch dni nie świadczy o niczym. Brak
            // propozycji jest tu właściwą odpowiedzią: wpis zostaje do opisania ręcznie,
            // zamiast udawać przelew, którego nie było.
            await AddAsync(db, "Revolut wspólny", Kwota, teraz.AddDays(-2));

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Twój Asystent", "500 PLN mniej na Twoim koncie - Przelew własny", teraz));

            db.ChangeTracker.Clear();
            (await db.Transactions.ToListAsync())
                .Should().OnlyContain(t => t.ProposedTransferGroupId == null);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_genuine_pair_is_still_proposed()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, ingest) = await SetupAsync(dbPath);
            var teraz = DateTimeOffset.UtcNow;

            // Kontrola negatywna: zwężenie okna nie może wyłączyć dobierania par tam,
            // gdzie przelew naprawdę był. Dwie godziny mieszczą się w oknie — tyle
            // potrafi się spóźnić „Twój Asystent" z ING.
            await AddAsync(db, "Revolut wspólny", Kwota, teraz.AddHours(-2));

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Twój Asystent", "500 PLN mniej na Twoim koncie - Przelew własny", teraz));

            (await PairedWithAsync(db)).Should().BeEquivalentTo(["ING osobisty", "Revolut wspólny"]);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task<string[]> PairedWithAsync(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        var konta = await db.Accounts.ToListAsync();
        var nogi = (await db.Transactions.ToListAsync())
            .Where(t => t.ProposedTransferGroupId is not null)
            .ToList();

        return [.. nogi.Select(t => konta.First(a => a.Id == t.AccountId).Name)];
    }

    private static async Task AddAsync(CastellanDbContext db, string konto, long grosze, DateTimeOffset kiedy)
    {
        db.ChangeTracker.Clear();
        var account = await db.Accounts.FirstAsync(a => a.Name == konto);
        db.Transactions.Add(Transaction.CreateManual(
            account.Id, new Money(grosze), kiedy, Category.UnsortedId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var kiedys = DateTimeOffset.UtcNow.AddYears(-1);
        db.Accounts.AddRange(
            Account.Create("ING osobisty",    AccountKind.Checking, new Money(1_000_000), kiedys, "ING"),
            Account.Create("OKO ING",         AccountKind.Savings,  new Money(1_000_000), kiedys, "ING"),
            Account.Create("Revolut wspólny", AccountKind.Checking, new Money(1_000_000), kiedys, "Revolut"));
        db.Categories.Add(Category.Create("Rezerwy", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, new IngestRawNotificationUseCase(
            new RawNotificationRepository(db),
            new AccountRepository(db),
            new TransactionRepository(db),
            new CategoryRuleRepository(db),
            new UnitOfWork(db),
            [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]));
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_parowanie_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
