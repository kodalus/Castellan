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
/// Reguluje regresję: re-savowanie planu miesiąca rzucało DbUpdateConcurrencyException
/// ("affected 0 rows"), mimo że to zupełnie zwyczajny przepływ (dwie osobne instancje
/// DbContext — nic wspólnego z długim życiem DbContext w MAUI). MonthBudget.Plan()
/// zawsze usuwa wszystkie koperty i tworzy je na nowo ze świeżym GUID-em; taki nowy
/// obiekt, dopięty tylko przez nawigację już śledzonego MonthBudget (bez jawnego
/// db.Add()), EF Core myli z encją istniejącą (Modified zamiast Added) i generuje
/// UPDATE dla wiersza, którego nigdy nie było w bazie.
/// </summary>
public class PlanMonthEnvelopeTrackingTest
{
    [Fact]
    public async Task Plan_can_be_resaved_from_a_fresh_DbContext_with_changed_envelope_amount()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_plan_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            CategoryId categoryId;
            var month = YearMonth.Current;

            using (var db1 = new CastellanDbContext(options))
            {
                db1.Database.Migrate();
                var category = Category.Create("Testowa", CategoryKind.Expense);
                db1.Categories.Add(category);
                await db1.SaveChangesAsync();
                categoryId = category.Id;

                var plan1 = new PlanMonthUseCase(new MonthBudgetRepository(db1), new UnitOfWork(db1));
                await plan1.ExecuteAsync(new PlanMonthUseCase.Input(
                    month, new Money(100_000), [new PlanMonthUseCase.EnvelopeInput(category.Id, new Money(50_000))]));
            }

            using var db2 = new CastellanDbContext(options);
            var plan2 = new PlanMonthUseCase(new MonthBudgetRepository(db2), new UnitOfWork(db2));

            var act = async () => await plan2.ExecuteAsync(new PlanMonthUseCase.Input(
                month, new Money(120_000), [new PlanMonthUseCase.EnvelopeInput(categoryId, new Money(60_000))]));

            await act.Should().NotThrowAsync();

            var reloaded = await new MonthBudgetRepository(db2).GetForMonthAsync(month);
            reloaded!.AvailableFunds.Grosze.Should().Be(120_000);
            reloaded.Envelopes.Single().PlannedAmount.Grosze.Should().Be(60_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
