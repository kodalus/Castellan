using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Repositories;
using Castellan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// MAUI nie tworzy nowego DI scope per nawigacja, więc CastellanDbContext żyje
/// przez całą sesję aplikacji. Import kopii zapasowej pisze surowym SQL (omija
/// tracker EF) na tej samej, długo żyjącej instancji — bez ChangeTracker.Clear()
/// po imporcie tracker mógłby trzymać encje sprzed importu, niezgodne z tym,
/// co surowe SQL właśnie zrobiło w bazie. W praktyce realny "affected 0 rows"
/// po re-save'owaniu planu miesiąca miał INNĄ przyczynę (patrz
/// PlanMonthEnvelopeTrackingTest), ale to nie unieważnia czyszczenia trackera
/// po surowym SQL jako uzasadnionego zabezpieczenia — ten test pilnuje, żeby
/// import+ponowny zapis planu (realistyczny, złożony scenariusz) nadal działał.
/// </summary>
public class StaleTrackerAfterImportTest
{
    [Fact]
    public async Task Plan_can_be_saved_again_after_a_raw_sql_import_on_the_same_long_lived_context()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_stale_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new CastellanDbContext(options);
            db.Database.Migrate();

            var category = Category.Create("Testowa", CategoryKind.Expense);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var budgetRepo = new MonthBudgetRepository(db);
            var uow = new UnitOfWork(db);
            var plan = new PlanMonthUseCase(budgetRepo, uow);

            var month = YearMonth.Current;

            // Pierwszy zapis planu — tworzy MonthBudget + kopertę, śledzone przez ten db.
            await plan.ExecuteAsync(new PlanMonthUseCase.Input(
                month, new Money(100_000), [new PlanMonthUseCase.EnvelopeInput(category.Id, new Money(50_000))]));

            // Import: surowe SQL na TYM SAMYM db — usuwa i wstawia od nowa,
            // z pominięciem trackera EF (dokładnie jak prawdziwy BackupService.ImportAsync).
            var backup = new BackupService(db);
            var exported = await backup.ExportAsync();
            exported.MonthBudgets.Should().ContainSingle();
            exported.MonthBudgets[0].Envelopes.Should().ContainSingle();

            await backup.ImportAsync(exported);

            var postImportBudgets = await db.MonthBudgets.AsNoTracking().ToListAsync();
            var postImportEnvelopes = await db.Set<Envelope>().AsNoTracking().ToListAsync();
            postImportBudgets.Should().ContainSingle("import powinien zachowac MonthBudget");
            postImportEnvelopes.Should().ContainSingle("import powinien zachowac koperte");

            // Drugi zapis planu na TYM SAMYM, długo żyjącym db — bez ChangeTracker.Clear()
            // w UnitOfWork ta operacja rzucała DbUpdateConcurrencyException.
            var act = async () => await plan.ExecuteAsync(new PlanMonthUseCase.Input(
                month, new Money(120_000), [new PlanMonthUseCase.EnvelopeInput(category.Id, new Money(60_000))]));

            await act.Should().NotThrowAsync();

            var reloaded = await budgetRepo.GetForMonthAsync(month);
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
