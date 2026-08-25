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
/// Zebrana kwota funduszu bywa rozjechana z rzeczywistością — odsetki na koncie, wpłata
/// sprzed założenia funduszu, wpłata zapisana dwa razy. Poprawka musi być możliwa, ale
/// nie może udawać wpłaty: zaliczenie jej jako wpłaty zamknęłoby bieżący okres
/// i podpowiadana rata zniknęłaby na miesiąc.
/// </summary>
public class FundBalanceEditTests
{
    private static async Task<(CastellanDbContext db, UpdateFundUseCase update, GetFundOverviewUseCase overview)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var fundRepo = new FundRepository(db);
        return (db, new UpdateFundUseCase(fundRepo, new UnitOfWork(db)), new GetFundOverviewUseCase(fundRepo));
    }

    private static Fund NewFund() =>
        Fund.Create("Wakacje", FundKind.Vacation, new Money(500_000),
            DateOnly.FromDateTime(DateTime.Today).AddMonths(10));

    private static UpdateFundCommand Command(Fund f, Money? balance = null) =>
        new(f.Id, f.Name, f.Kind, f.TargetAmount, f.Deadline, balance);

    [Fact]
    public async Task Correcting_the_balance_saves_the_new_amount()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_fbal_{Guid.NewGuid():N}.db");
        try
        {
            var (db, update, _) = await SetupAsync(dbPath);

            var fund = NewFund();
            fund.Contribute(new Money(100_000));
            db.Funds.Add(fund);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await update.ExecuteAsync(Command(fund, new Money(137_450)));
            db.ChangeTracker.Clear();

            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(137_450);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_correction_does_not_count_as_this_months_contribution()
    {
        // Sedno: po prawdziwej wpłacie bieżący okres jest zamknięty i rata na ten miesiąc
        // znika. Poprawka salda nie ma prawa wywołać tego samego skutku.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_fbal_{Guid.NewGuid():N}.db");
        try
        {
            var (db, update, overview) = await SetupAsync(dbPath);

            var fund = NewFund();
            db.Funds.Add(fund);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var before = (await overview.ExecuteAsync(paydateDay: 0)).Items.Single();

            await update.ExecuteAsync(Command(fund, new Money(50_000)));
            db.ChangeTracker.Clear();

            var after = (await overview.ExecuteAsync(paydateDay: 0)).Items.Single();

            (await db.Funds.SingleAsync()).LastContributionMonth.Should().BeNull(
                "poprawka to nie wpłata");
            after.PeriodsRemaining.Should().Be(before.PeriodsRemaining,
                "liczba rat do terminu nie może zmaleć przez samo wyrównanie salda");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Editing_other_fields_without_a_balance_leaves_it_alone()
    {
        // Zmiana nazwy czy celu nie ma ruszać zebranej kwoty — to dwie różne czynności.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_fbal_{Guid.NewGuid():N}.db");
        try
        {
            var (db, update, _) = await SetupAsync(dbPath);

            var fund = NewFund();
            fund.Contribute(new Money(100_000));
            db.Funds.Add(fund);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await update.ExecuteAsync(new UpdateFundCommand(
                fund.Id, "Wakacje 2027", FundKind.Vacation, new Money(800_000), fund.Deadline));
            db.ChangeTracker.Clear();

            var saved = await db.Funds.SingleAsync();
            saved.Name.Should().Be("Wakacje 2027");
            saved.TargetAmount.Grosze.Should().Be(800_000);
            saved.Balance.Grosze.Should().Be(100_000);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Zero_is_a_valid_correction_but_a_negative_amount_is_not()
    {
        // Wyzerowanie funduszu jest sensowne (pomyłka przy zakładaniu), ujemne zebrane
        // nie znaczy nic — od długów jest osobny agregat.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_fbal_{Guid.NewGuid():N}.db");
        try
        {
            var (db, update, _) = await SetupAsync(dbPath);

            var fund = NewFund();
            fund.Contribute(new Money(100_000));
            db.Funds.Add(fund);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await update.ExecuteAsync(Command(fund, Money.Zero));
            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(0);

            var act = async () => await update.ExecuteAsync(Command(fund, new Money(-1)));
            await act.Should().ThrowAsync<ArgumentException>();
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
