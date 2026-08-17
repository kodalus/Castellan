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
/// Symulacja kuli śnieżnej: nadwyżka ponad minimalne raty idzie w najmniejsze saldo,
/// a rata spłaconego długu dołącza do nadwyżki i przyspiesza kolejny. Ten efekt kaskady
/// jest całym sensem obliczenia — bez niego wystarczyłoby dzielenie salda przez ratę.
/// </summary>
public class DebtPayoffSimulationTest
{
    private static async Task<CastellanDbContext> SetupAsync(string dbPath, params Debt[] debts)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();
        db.Debts.AddRange(debts);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Default_budget_is_the_sum_of_installments()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_sim_{Guid.NewGuid():N}.db");
        try
        {
            using var db = await SetupAsync(dbPath,
                Debt.Create("Telefon",   DebtKind.Installment, new Money(100_000), new Money(20_000)),
                Debt.Create("Gotówkowy", DebtKind.CashLoan,    new Money(600_000), new Money(30_000)));

            var plan = await new SimulateDebtPayoffUseCase(new DebtRepository(db)).ExecuteAsync();

            plan.TotalDebt.Grosze.Should().Be(700_000);
            plan.MinimumMonthly.Grosze.Should().Be(50_000);
            plan.SimulatedMonthly.Grosze.Should().Be(50_000);
            plan.BelowMinimum.Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Snowball_beats_paying_each_debt_on_its_own_schedule()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_sim_{Guid.NewGuid():N}.db");
        try
        {
            // Osobno: telefon 5 mies. (100k / 20k), gotówkowy 20 mies. (600k / 30k).
            // Razem przy 50k/mies. cały dług to 700k / 50k = 14 miesięcy — bo rata
            // spłaconego telefonu od 6. miesiąca dokłada się do gotówkowego.
            using var db = await SetupAsync(dbPath,
                Debt.Create("Telefon",   DebtKind.Installment, new Money(100_000), new Money(20_000)),
                Debt.Create("Gotówkowy", DebtKind.CashLoan,    new Money(600_000), new Money(30_000)));

            var plan = await new SimulateDebtPayoffUseCase(new DebtRepository(db)).ExecuteAsync();

            plan.MonthsToFreedom.Should().Be(14, "kaskada skraca spłatę względem 20 miesięcy osobnego harmonogramu");

            plan.Steps.Should().HaveCount(2);

            // Przy budżecie równym sumie rat nie ma nadwyżki, więc telefon spłaca się
            // dokładnie swoją ratą: 100 000 / 20 000 = 5 miesięcy.
            plan.Steps[0].Name.Should().Be("Telefon");
            plan.Steps[0].MonthCleared.Should().Be(5);

            // Kaskada zaczyna działać dopiero teraz: od 6. miesiąca rata telefonu
            // dokłada się do gotówkowego (30 000 + 20 000), więc pozostałe 450 000
            // znika w 9 miesięcy zamiast w 15.
            plan.Steps[1].Name.Should().Be("Gotówkowy");
            plan.Steps[1].MonthCleared.Should().Be(14);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Paying_more_shortens_the_road()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_sim_{Guid.NewGuid():N}.db");
        try
        {
            using var db = await SetupAsync(dbPath,
                Debt.Create("Gotówkowy", DebtKind.CashLoan, new Money(600_000), new Money(30_000)));

            var simulate = new SimulateDebtPayoffUseCase(new DebtRepository(db));

            var baseline = await simulate.ExecuteAsync();
            var faster   = await simulate.ExecuteAsync(new Money(60_000));

            baseline.MonthsToFreedom.Should().Be(20);
            faster.MonthsToFreedom.Should().Be(10);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Budget_below_the_minimum_is_flagged_instead_of_silently_stretching()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_sim_{Guid.NewGuid():N}.db");
        try
        {
            using var db = await SetupAsync(dbPath,
                Debt.Create("Gotówkowy", DebtKind.CashLoan, new Money(600_000), new Money(30_000)));

            var plan = await new SimulateDebtPayoffUseCase(new DebtRepository(db))
                .ExecuteAsync(new Money(10_000));

            plan.BelowMinimum.Should().BeTrue("użytkowniczka musi wiedzieć, że nie obsłuży rat");
            plan.MonthsToFreedom.Should().Be(60);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Debt_without_an_installment_still_gets_paid_from_the_surplus()
    {
        // Pożyczka od rodziny bez harmonogramu nie może zablokować symulacji —
        // dostaje nadwyżkę i też ma swoją datę.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_sim_{Guid.NewGuid():N}.db");
        try
        {
            using var db = await SetupAsync(dbPath,
                Debt.Create("Od mamy", DebtKind.FromFamily, new Money(100_000), Money.Zero));

            var plan = await new SimulateDebtPayoffUseCase(new DebtRepository(db))
                .ExecuteAsync(new Money(25_000));

            plan.MonthsToFreedom.Should().Be(4);
            plan.Steps.Single().Name.Should().Be("Od mamy");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task No_debts_means_no_plan_to_confront()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_sim_{Guid.NewGuid():N}.db");
        try
        {
            using var db = await SetupAsync(dbPath);

            var plan = await new SimulateDebtPayoffUseCase(new DebtRepository(db)).ExecuteAsync();

            plan.TotalDebt.Grosze.Should().Be(0);
            plan.MonthsToFreedom.Should().Be(0);
            plan.Steps.Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
