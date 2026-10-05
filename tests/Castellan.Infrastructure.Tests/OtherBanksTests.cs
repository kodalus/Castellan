using Castellan.Application.Parsers;
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
/// Ustawienie banku przy koncie działa także dla banków, których powiadomień aplikacja
/// nie czyta — i to nie jest martwa etykieta.
///
/// Portfel Google nazywa kartę w treści powiadomienia („karta Millennium"), więc to
/// jedyny ślad, po którym da się rozpoznać, z którego konta poszła płatność telefonem.
/// Dopóki lista banków miała dwie pozycje, każda taka płatność trafiała na konto wybrane
/// domyślnie — czyli pierwsze rozliczeniowe alfabetycznie, bez związku z kartą.
/// </summary>
public class OtherBanksTests
{
    [Fact]
    public async Task A_wallet_payment_with_a_card_from_another_bank_lands_on_that_banks_account()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, ingest) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow.AddYears(-1);

            // „ING" stoi w alfabecie przed „Osobiste", wiec reguła domyślna wybrałaby
            // właśnie je — test rozstrzyga, czy zadecydowała karta, czy kolejność.
            db.Accounts.Add(Account.Create("ING", AccountKind.Checking, Money.Zero, now, Banks.Ing));
            db.Accounts.Add(Account.Create("Osobiste", AccountKind.Checking, Money.Zero, now, "Millennium"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                "com.google.android.apps.walletnfcrel", "LIDL 2306",
                "Kwota 50,00 zł – karta Millennium", DateTimeOffset.UtcNow));

            var tx = await db.Transactions.SingleAsync();
            var account = await db.Accounts.SingleAsync(a => a.Id == tx.AccountId);

            account.Name.Should().Be("Osobiste");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_list_of_banks_covers_more_than_the_two_with_notification_support()
    {
        // Czytanie powiadomień wymaga parsera pod konkretny format wiadomości, więc
        // tych banków będą zawsze dwa albo trzy. Wybór banku przy koncie to inna
        // sprawa — i nie ma powodu, żeby był tak samo wąski.
        await Task.CompletedTask;

        Banks.WithNotificationSupport.Should().HaveCount(2);
        Banks.Popular.Should().Contain(Banks.WithNotificationSupport);
        Banks.Popular.Length.Should().BeGreaterThan(Banks.WithNotificationSupport.Length);
        Banks.Popular.Should().OnlyHaveUniqueItems();
    }

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var ingest = new IngestRawNotificationUseCase(
            new RawNotificationRepository(db),
            new AccountRepository(db),
            new TransactionRepository(db),
            new CategoryRuleRepository(db),
            new UnitOfWork(db),
            [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]);

        return (db, ingest);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_banks_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
