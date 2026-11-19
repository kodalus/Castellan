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
/// Saldo konta liczy się od ostatniego uzgodnienia, więc transakcja z wcześniejszą datą
/// zapisuje się poprawnie, ale salda nie rusza. Reguła jest słuszna, jej milczenie — nie:
/// użytkowniczka zgłosiła przelew, po którym jedno konto zmieniło saldo, a drugie nie,
/// i nic tego nie tłumaczyło.
///
/// Te testy pilnują, żeby ostrzeżenie mówiło DOKŁADNIE to samo, co robi liczenie salda.
/// Gdyby się rozjechały, aplikacja ostrzegałaby o czymś, co się dzieje, albo milczała
/// o czymś, co się dzieje naprawdę — jedno i drugie gorsze niż brak ostrzeżenia.
/// </summary>
public class BalanceImpactTests
{
    private static readonly DateTimeOffset Uzgodnienie = new(2026, 11, 19, 10, 26, 51, TimeSpan.FromHours(1));
    private const long Saldo = 35_016;
    private const long Kwota = 70_000;

    [Fact]
    public async Task An_entry_from_before_the_reconciliation_is_reported_and_the_balance_really_stays_put()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var konto = await KontoAsync(db, "ING osobisty", Uzgodnienie);

            var kiedy = Uzgodnienie.AddMinutes(-2);
            await AddAsync(db, konto, -Kwota, kiedy);

            var ostrzezenia = await CheckAsync(db, (konto, kiedy));

            ostrzezenia.Should().ContainSingle()
                .Which.AccountName.Should().Be("ING osobisty");

            // Druga połowa testu: ostrzeżenie nie jest prognozą, tylko opisem faktu.
            (await SaldoAsync(db, konto)).Grosze.Should().Be(Saldo,
                "uzgodnienie przyjęło, że ta kwota już jest w saldzie");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task An_entry_from_after_it_is_silent_and_the_balance_moves()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var konto = await KontoAsync(db, "ING osobisty", Uzgodnienie);

            var kiedy = Uzgodnienie.AddMinutes(2);
            await AddAsync(db, konto, -Kwota, kiedy);

            (await CheckAsync(db, (konto, kiedy))).Should().BeEmpty();
            (await SaldoAsync(db, konto)).Grosze.Should().Be(Saldo - Kwota);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_exact_moment_of_the_reconciliation_counts_as_before_it()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var konto = await KontoAsync(db, "ING osobisty", Uzgodnienie);

            // Granica musi być ta sama po obu stronach: saldo bierze transakcje ostro
            // PÓŹNIEJSZE, więc co do sekundy równa z uzgodnieniem się nie liczy.
            await AddAsync(db, konto, -Kwota, Uzgodnienie);

            (await CheckAsync(db, (konto, Uzgodnienie))).Should().ContainSingle();
            (await SaldoAsync(db, konto)).Grosze.Should().Be(Saldo);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_transfer_reports_only_the_leg_that_will_be_ignored()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);

            // Dokładnie układ z 19.11: ING uzgodnione dziś rano, Revolut dwa tygodnie temu.
            var ing = await KontoAsync(db, "ING osobisty", Uzgodnienie);
            var revolut = await KontoAsync(db, "Revolut wspólny", Uzgodnienie.AddDays(-14));

            var kiedy = Uzgodnienie.AddMinutes(-2);
            await AddAsync(db, ing, -Kwota, kiedy);
            await AddAsync(db, revolut, Kwota, kiedy);

            var ostrzezenia = await CheckAsync(db, (ing, kiedy), (revolut, kiedy));

            ostrzezenia.Should().ContainSingle()
                .Which.AccountName.Should().Be("ING osobisty");

            // To jest właśnie ta szkoda, o której ostrzeżenie ma powiedzieć: przelew nie
            // zmienia majątku, a tutaj suma kont rośnie o całą kwotę przelewu.
            var razem = (await SaldoAsync(db, ing)).Grosze + (await SaldoAsync(db, revolut)).Grosze;
            razem.Should().Be(Saldo + Saldo + Kwota);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_transfer_between_two_freshly_reconciled_accounts_says_nothing()
    {
        var dbPath = NewDbPath();
        try
        {
            using var db = await SetupAsync(dbPath);
            var ing = await KontoAsync(db, "ING osobisty", Uzgodnienie);
            var revolut = await KontoAsync(db, "Revolut wspólny", Uzgodnienie);

            var kiedy = Uzgodnienie.AddHours(1);

            (await CheckAsync(db, (ing, kiedy), (revolut, kiedy))).Should().BeEmpty(
                "zwykły przelew z dzisiejszą datą nie ma o czym ostrzegać");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static Task<IReadOnlyList<CheckBalanceImpactUseCase.Ignored>> CheckAsync(
        CastellanDbContext db, params (AccountId, DateTimeOffset)[] wpisy)
    {
        db.ChangeTracker.Clear();
        return new CheckBalanceImpactUseCase(new AccountRepository(db)).ExecuteAsync(wpisy);
    }

    private static async Task<Money> SaldoAsync(CastellanDbContext db, AccountId konto)
    {
        db.ChangeTracker.Clear();
        var salda = await new GetAccountsWithBalancesUseCase(
            new AccountRepository(db), new TransactionRepository(db)).ExecuteAsync();
        return salda.Single(a => a.Id == konto).CurrentBalance;
    }

    private static async Task<AccountId> KontoAsync(CastellanDbContext db, string nazwa, DateTimeOffset uzgodnione)
    {
        var account = Account.Create(nazwa, AccountKind.Checking, new Money(Saldo), uzgodnione);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return account.Id;
    }

    private static async Task AddAsync(CastellanDbContext db, AccountId konto, long grosze, DateTimeOffset kiedy)
    {
        db.ChangeTracker.Clear();
        db.Transactions.Add(Transaction.CreateManual(konto, new Money(grosze), kiedy, Category.TransferId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<CastellanDbContext> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_saldo_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
