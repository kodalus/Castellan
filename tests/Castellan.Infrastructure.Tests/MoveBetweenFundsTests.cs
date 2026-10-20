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
/// Kilka celów na jednym koncie oszczędnościowym to normalna sytuacja, a przełożenie
/// pieniędzy z jednego celu na drugi NIE RUSZA żadnego konta — fundusz jest kopertą nad
/// pieniędzmi, które i tak tam leżą.
///
/// Obejście przez dwa przelewy (oszczędnościowe → rozliczeniowe → oszczędnościowe) daje
/// ten sam efekt na funduszach, ale zostawia cztery wpisy w historii konta, które nic
/// nie opisują: żadne pieniądze nigdzie nie poszły.
/// </summary>
public class MoveBetweenFundsTests
{
    private const long Wakacje = 300_000;
    private const long Remont = 100_000;
    private const long Przenoszone = 120_000;

    [Fact]
    public async Task Moving_between_goals_changes_only_the_two_balances()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, wakacje, remont) = await SetupAsync(dbPath);

            await Move(db).ExecuteAsync(wakacje, remont, new Money(Przenoszone));

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync(f => f.Id == wakacje)).Balance.Grosze
                .Should().Be(Wakacje - Przenoszone);
            (await db.Funds.SingleAsync(f => f.Id == remont)).Balance.Grosze
                .Should().Be(Remont + Przenoszone);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Moving_between_goals_creates_no_transaction_at_all()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, wakacje, remont) = await SetupAsync(dbPath);

            await Move(db).ExecuteAsync(wakacje, remont, new Money(Przenoszone));

            db.ChangeTracker.Clear();

            // To jest cała istota tej operacji: pieniądze nie ruszyły się z konta, więc
            // historia konta nie ma o czym mówić, a budżet miesiąca zostaje nietknięty.
            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_total_put_aside_does_not_change()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, wakacje, remont) = await SetupAsync(dbPath);

            await Move(db).ExecuteAsync(wakacje, remont, new Money(Przenoszone));

            db.ChangeTracker.Clear();
            // Suma liczona w pamieci: Money jest konwertowane przy zapisie, wiec SumAsync
            // po jego wnetrzu nie ma sie na czym oprzec po stronie SQL.
            var salda = await db.Funds.ToListAsync();
            salda.Sum(f => f.Balance.Grosze).Should().Be(Wakacje + Remont);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Moving_more_than_a_goal_holds_is_refused()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, wakacje, remont) = await SetupAsync(dbPath);

            // Bez tego saldo zeszłoby poniżej zera — liczby, której żaden ekran nie umie
            // sensownie pokazać, a która znaczyłaby odłożone minus pieniądze.
            var act = async () => await Move(db).ExecuteAsync(remont, wakacje, new Money(Remont + 1));

            await act.Should().ThrowAsync<InvalidOperationException>();

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync(f => f.Id == remont)).Balance.Grosze.Should().Be(Remont);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Moving_onto_itself_is_refused()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, wakacje, _) = await SetupAsync(dbPath);

            var act = async () => await Move(db).ExecuteAsync(wakacje, wakacje, new Money(Przenoszone));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static MoveBetweenFundsUseCase Move(CastellanDbContext db) =>
        new(new FundRepository(db), new UnitOfWork(db));

    private static async Task<(CastellanDbContext db, FundId wakacje, FundId remont)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var wakacje = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_000_000), null);
        wakacje.Contribute(new Money(Wakacje));

        var remont = Fund.Create("Remont", FundKind.Custom, new Money(2_000_000), null);
        remont.Contribute(new Money(Remont));

        db.Funds.AddRange(wakacje, remont);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, wakacje.Id, remont.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_fundusze_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
