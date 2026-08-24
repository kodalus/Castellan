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
/// Płatność kartą ING telefonem zgłaszają dwie aplikacje: Portfel Google i bank.
/// Deduplikacja porównuje kandydatów w obrębie JEDNEGO konta, więc obie strony pary
/// muszą trafić na to samo konto — inaczej nie ma czego z czym porównać.
///
/// Dwa realne sposoby, na jakie ta para się rozjeżdżała, i oba dają ten sam objaw:
/// dwie transakcje na tę samą kwotę.
/// </summary>
public class IngWalletDuplicateTests
{
    private const string IngPackage = "pl.ing.mojeing";
    private const string WalletPackage = "com.google.android.apps.walletnfcrel";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase useCase)>
        SetupAsync(string dbPath, string ingAccountName, string otherAccountName)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        // Konta są zwracane posortowane po nazwie, więc awaryjne „pierwsze konto
        // rozliczeniowe" to konto alfabetycznie wcześniejsze. Nazwy w testach są tak
        // dobrane, żeby to NIE było konto ING — inaczej błędne dopasowanie trafiałoby
        // w ING przypadkiem i test niczego by nie pilnował.
        db.Accounts.Add(Account.Create(otherAccountName, AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1)));
        db.Accounts.Add(Account.Create(ingAccountName, AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1)));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var useCase = new IngestRawNotificationUseCase(
            new RawNotificationRepository(db),
            new AccountRepository(db),
            new TransactionRepository(db),
            new CategoryRuleRepository(db),
            new UnitOfWork(db),
            [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]);

        return (db, useCase);
    }

    [Fact]
    public async Task Wallet_finds_the_ing_account_even_when_its_name_is_longer_than_the_card_name()
    {
        // Konto nazwane szerzej niż karta: podpowiedź „ING" nie zawiera nazwy
        // „ING Konto z Lwem", więc dopasowanie jednostronne zawodziło i Portfel lądował
        // na pierwszym koncie rozliczeniowym — tutaj na koncie Alior.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dup_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, "ING Konto z Lwem", "Alior wspólne");
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                WalletPackage, "JMP S.A. BIEDRONKA 591", "Kwota 87,87 zł – karta ING", now));

            var tx = await db.Transactions.SingleAsync();
            var account = await db.Accounts.SingleAsync(a => a.Id == tx.AccountId);
            account.Name.Should().Be("ING Konto z Lwem", "podpowiedź wskazuje ING, a nie pierwsze konto z brzegu");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Ing_and_wallet_collapse_into_one_transaction_despite_the_account_naming()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dup_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, "ING Konto z Lwem", "Alior wspólne");
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                WalletPackage, "JMP S.A. BIEDRONKA 591", "Kwota 87,87 zł – karta ING", now));

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Moje ING. Twój Asystent",
                "87,87 PLN mniej na Twoim koncie - Biedronka", now.AddMinutes(2)));

            (await db.Transactions.CountAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_delayed_bank_notification_still_collapses_into_the_wallet_one()
    {
        // „Twój Asystent" nie przychodzi od razu po płatności. Przy oknie
        // kilkunastominutowym para gubiła się i powstawał duplikat.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dup_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, "ING", "Alior wspólne");
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                WalletPackage, "JMP S.A. BIEDRONKA 591", "Kwota 87,87 zł – karta ING", now));

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Moje ING. Twój Asystent",
                "87,87 PLN mniej na Twoim koncie - Biedronka", now.AddMinutes(90)));

            (await db.Transactions.CountAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Two_separate_payments_of_the_same_amount_from_one_source_stay_separate()
    {
        // Kontrola negatywna dla szerokiego okna: rozroznienie po zrodle nie moze
        // sklejac dwoch prawdziwych platnosci zgloszonych przez TE SAMA aplikacje.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_dup_{Guid.NewGuid():N}.db");
        try
        {
            var (db, useCase) = await SetupAsync(dbPath, "ING", "Alior wspólne");
            var now = DateTimeOffset.UtcNow;

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Moje ING. Twój Asystent",
                "87,87 PLN mniej na Twoim koncie - Biedronka", now));

            await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Moje ING. Twój Asystent",
                "87,87 PLN mniej na Twoim koncie - Piekarnia", now.AddMinutes(60)));

            (await db.Transactions.CountAsync()).Should().Be(2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
