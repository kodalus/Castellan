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
/// Raport porównuje zapisaną kwotę z tym, co dzisiejszy wzorzec odczyta z zachowanej
/// treści powiadomienia. Najważniejsza gwarancja: transakcja poprawiona ręcznie NIE
/// pojawia się na liście, bo jej kwota zgadza się już z powiadomieniem — raport z
/// założenia nie potrafi zaproponować cofnięcia poprawnej korekty.
/// </summary>
public class NotificationAmountAuditTest
{
    private const string IngPackage = "pl.ing.mojeing";

    // Kwota z twardą spacją — dokładnie ten przypadek, przez który powstawały
    // zaniżone transakcje.
    private const string BigAmountText =
        "1 600,00 PLN więcej na Twoim koncie - Direct Rika";

    private static async Task<(CastellanDbContext db, AuditNotificationAmountsUseCase audit, Account account, Category category)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var account = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1));
        var category = Category.Create("Wypłata", CategoryKind.Income);
        db.Accounts.Add(account);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var audit = new AuditNotificationAmountsUseCase(
            new RawNotificationRepository(db),
            new TransactionRepository(db),
            new INotificationParser[] { new IngNotificationParser() });

        return (db, audit, account, category);
    }

    /// <summary>Zapisuje transakcję o podanej kwocie i powiązane z nią powiadomienie.</summary>
    private static async Task SeedAsync(CastellanDbContext db, Account account, Category category, long grosze)
    {
        var notification = RawNotification.CreateUnparsed(
            IngPackage, "Moje ING. Twój Asystent", BigAmountText, DateTimeOffset.UtcNow.AddDays(-3));

        var tx = Transaction.CreateManual(
            account.Id, new Money(grosze), DateTimeOffset.UtcNow.AddDays(-3), category.Id);

        notification.MarkParsed(tx.Id);
        db.Transactions.Add(tx);
        db.RawNotifications.Add(notification);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Reports_a_transaction_that_still_holds_the_understated_amount()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_audit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, audit, account, category) = await SetupAsync(dbPath);
            await SeedAsync(db, account, category, 60_000); // zapisane 600 zł

            var result = await audit.ExecuteAsync();

            result.CheckedCount.Should().Be(1);
            var row = result.Discrepancies.Should().ContainSingle().Subject;
            row.Stored.Grosze.Should().Be(60_000);
            row.FromNotification.Grosze.Should().Be(160_000);
            row.Difference.Grosze.Should().Be(100_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Stays_silent_about_a_transaction_the_user_already_corrected()
    {
        // To jest cała obietnica dana użytkowniczce: reczne poprawki sa nie do ruszenia,
        // bo poprawiona kwota zgadza sie z powiadomieniem i nie tworzy rozbieznosci.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_audit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, audit, account, category) = await SetupAsync(dbPath);
            await SeedAsync(db, account, category, 160_000); // poprawione recznie na 1600 zł

            var result = await audit.ExecuteAsync();

            result.CheckedCount.Should().Be(1);
            result.Discrepancies.Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Correcting_a_row_removes_it_from_the_next_report()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_audit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, audit, account, category) = await SetupAsync(dbPath);
            await SeedAsync(db, account, category, 60_000);

            var before = await audit.ExecuteAsync();
            var row = before.Discrepancies.Should().ContainSingle().Subject;
            row.IsCorrectable.Should().BeTrue();

            await new CorrectTransactionAmountUseCase(new TransactionRepository(db), new UnitOfWork(db))
                .ExecuteAsync(row.TransactionId, row.FromNotification);
            db.ChangeTracker.Clear();

            (await db.Transactions.SingleAsync()).Amount.Grosze.Should().Be(160_000);
            (await audit.ExecuteAsync()).Discrepancies.Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Refuses_to_correct_a_transaction_paid_from_a_fund()
    {
        // Fundusz zdjal juz stara kwote ze swojego salda, wiec zmiana samej transakcji
        // rozjechalaby saldo funduszu w miejscu, ktorego uzytkownik teraz nie widzi.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_audit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, audit, account, category) = await SetupAsync(dbPath);

            var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(6));
            db.Funds.Add(fund);

            var tx = Transaction.CreateManual(
                account.Id, new Money(60_000), DateTimeOffset.UtcNow.AddDays(-2), category.Id);
            tx.PayFromFund(fund.Id);
            var notification = RawNotification.CreateUnparsed(
                IngPackage, "Moje ING. Twój Asystent", BigAmountText, DateTimeOffset.UtcNow.AddDays(-2));
            notification.MarkParsed(tx.Id);
            db.Transactions.Add(tx);
            db.RawNotifications.Add(notification);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await audit.ExecuteAsync();
            var row = result.Discrepancies.Should().ContainSingle().Subject;
            row.IsCorrectable.Should().BeFalse("kwota ma powiazania poza sama transakcja");

            var correct = new CorrectTransactionAmountUseCase(new TransactionRepository(db), new UnitOfWork(db));
            var act = async () => await correct.ExecuteAsync(row.TransactionId, row.FromNotification);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Skips_a_notification_whose_transaction_was_deleted()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_audit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, audit, account, category) = await SetupAsync(dbPath);
            await SeedAsync(db, account, category, 60_000);

            db.Transactions.RemoveRange(await db.Transactions.ToListAsync());
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await audit.ExecuteAsync();

            result.CheckedCount.Should().Be(0);
            result.Discrepancies.Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Counts_notifications_the_current_parser_cannot_read()
    {
        // „Nic nie znaleziono" musi dac sie odroznic od „nie dalo sie sprawdzic".
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_audit_{Guid.NewGuid():N}.db");
        try
        {
            var (db, audit, account, category) = await SetupAsync(dbPath);

            var tx = Transaction.CreateManual(
                account.Id, new Money(-1234), DateTimeOffset.UtcNow, category.Id);
            var notification = RawNotification.CreateUnparsed(
                IngPackage, "Moje ING", "Zupełnie inna treść bez kwoty", DateTimeOffset.UtcNow);
            notification.MarkParsed(tx.Id);
            db.Transactions.Add(tx);
            db.RawNotifications.Add(notification);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await audit.ExecuteAsync();

            result.UnreadableCount.Should().Be(1);
            result.CheckedCount.Should().Be(0);
            result.Discrepancies.Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
