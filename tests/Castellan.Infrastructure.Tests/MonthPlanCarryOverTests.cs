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
/// Planując nowy miesiąc, każda koperta startowała od zera — kilkanaście kategorii do
/// przepisania co miesiąc, mimo że wydatki stałe się nie zmieniają.
///
/// Punkt wyjścia z poprzedniego miesiąca pojawia się TYLKO wtedy, gdy planu nie ma.
/// To nie jest ostrożność na wyrost: gdyby wchodził też do zaplanowanego miesiąca,
/// koperta świadomie ustawiona na zero wracałaby z poprzednią kwotą przy każdym wejściu
/// na ekran i po cichu nadpisywała decyzję.
/// </summary>
public class MonthPlanCarryOverTests
{
    private static readonly YearMonth Sierpien = new(2026, 8);
    private static readonly YearMonth Wrzesien = new(2026, 9);

    private static async Task<(CastellanDbContext db, GetMonthPlanDraftUseCase draft, Category food, Category fun)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var food = Category.Create("Produkty do domu", CategoryKind.Expense);
        var fun = Category.Create("Rozrywka", CategoryKind.Expense);
        db.Categories.AddRange(food, fun);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, new GetMonthPlanDraftUseCase(new MonthBudgetRepository(db)), food, fun);
    }

    private static async Task PlanAsync(
        CastellanDbContext db, YearMonth month, CategoryId category, long grosze, long available = 500_000)
    {
        var budget = MonthBudget.Create(month, new Money(available));
        budget.Plan(category, new Money(grosze));
        db.MonthBudgets.Add(budget);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task An_unplanned_month_starts_from_the_previous_one()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_carry_{Guid.NewGuid():N}.db");
        try
        {
            var (db, draft, food, _) = await SetupAsync(dbPath);
            await PlanAsync(db, Sierpien, food.Id, 120_000);

            var wrzesien = await draft.ExecuteAsync(Wrzesien);

            wrzesien.Budget.Should().BeNull("wrzesień nie jest jeszcze zaplanowany");
            wrzesien.IsCarriedOver.Should().BeTrue();
            wrzesien.Template!.Envelopes.Single(e => e.CategoryId == food.Id)
                .PlannedAmount.Grosze.Should().Be(120_000);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_month_already_planned_is_never_overwritten()
    {
        // Sedno: wrzesień ma kopertę na 0 zł, sierpień na 1 200 zł. Punkt wyjścia nie
        // ma prawa się tu pojawić, bo zero jest decyzją, nie brakiem danych.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_carry_{Guid.NewGuid():N}.db");
        try
        {
            var (db, draft, food, _) = await SetupAsync(dbPath);
            await PlanAsync(db, Sierpien, food.Id, 120_000);
            await PlanAsync(db, Wrzesien, food.Id, 0);

            var wrzesien = await draft.ExecuteAsync(Wrzesien);

            wrzesien.IsCarriedOver.Should().BeFalse();
            wrzesien.Template.Should().BeNull("plan poprzedniego miesiąca nie jest nawet czytany");
            wrzesien.Budget!.Envelopes.Single(e => e.CategoryId == food.Id)
                .PlannedAmount.Grosze.Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_very_first_month_has_nothing_to_carry_over()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_carry_{Guid.NewGuid():N}.db");
        try
        {
            var (_, draft, _, _) = await SetupAsync(dbPath);

            var sierpien = await draft.ExecuteAsync(Sierpien);

            sierpien.Budget.Should().BeNull();
            sierpien.Template.Should().BeNull();
            sierpien.IsCarriedOver.Should().BeFalse("nie ma czego przenieść, więc nie ma o czym informować");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Only_the_month_directly_before_counts()
    {
        // Luka w planowaniu nie ma sie przenosic przez pol roku wstecz: plan sprzed
        // wielu miesiecy jest z innego zycia i podpowiadanie go szkodzi bardziej,
        // niz pomaga.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_carry_{Guid.NewGuid():N}.db");
        try
        {
            var (db, draft, food, _) = await SetupAsync(dbPath);
            await PlanAsync(db, new YearMonth(2026, 3), food.Id, 120_000);

            var wrzesien = await draft.ExecuteAsync(Wrzesien);

            wrzesien.IsCarriedOver.Should().BeFalse();
            wrzesien.Template.Should().BeNull();
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
