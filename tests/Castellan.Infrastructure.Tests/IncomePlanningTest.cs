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
/// Realny scenariusz użytkowniczki: trzy źródła przychodu (wypłata, wpłata
/// małżonka na wspólne konto, 800+) oraz własny przelew 2000 zł z konta
/// wypłaty na wspólne konto — ten ostatni to przesunięcie własnych pieniędzy
/// i NIE MOŻE być liczony jako przychód.
/// </summary>
public class IncomePlanningTest
{
    [Fact]
    public async Task Planned_and_actual_income_are_compared_per_source_and_transfers_are_excluded()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_income_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new CastellanDbContext(options);
            db.Database.Migrate();

            var ing     = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddMonths(-2));
            var revolut = Account.Create("Revolut", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddMonths(-2));

            var wyplata = Category.Create("Wypłata", CategoryKind.Income);
            var malzonek = Category.Create("Wpłata małżonka", CategoryKind.Income);
            var osiemset = Category.Create("800+", CategoryKind.Income);
            // Źródło bez planu i bez wpływów — zestawienie i tak ma je pokazać.
            var inne = Category.Create("Inne", CategoryKind.Income);

            db.Accounts.AddRange(ing, revolut);
            db.Categories.AddRange(wyplata, malzonek, osiemset, inne);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var month = YearMonth.Current;
            var now = DateTimeOffset.Now;

            var plan = new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db));
            await plan.ExecuteAsync(new PlanMonthUseCase.Input(
                month,
                new Money(1_000_000),
                Envelopes: [],
                Incomes:
                [
                    new PlanMonthUseCase.IncomeInput(wyplata.Id,  new Money(387_000)),
                    new PlanMonthUseCase.IncomeInput(malzonek.Id, new Money(200_000)),
                    new PlanMonthUseCase.IncomeInput(osiemset.Id, new Money(160_000)),
                ]));

            // Faktyczne wpływy: wypłata niższa niż plan, małżonek zgodnie z planem,
            // 800+ zgodnie z planem.
            db.Transactions.AddRange(
                Transaction.CreateManual(ing.Id,     new Money(350_000), now, wyplata.Id),
                Transaction.CreateManual(revolut.Id, new Money(200_000), now, malzonek.Id),
                Transaction.CreateManual(ing.Id,     new Money(160_000), now, osiemset.Id));

            // Własny przelew ING → Revolut: dwie strony, oznaczone jako transfer.
            var transferGroup = Guid.NewGuid();
            var outgoing = Transaction.CreateManual(ing.Id,     new Money(-200_000), now, wyplata.Id);
            var incoming = Transaction.CreateManual(revolut.Id, new Money(200_000),  now, wyplata.Id);
            outgoing.SetTransferGroup(transferGroup);
            incoming.SetTransferGroup(transferGroup);
            db.Transactions.AddRange(outgoing, incoming);

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var overview = new GetMonthOverviewUseCase(
                new MonthBudgetRepository(db), new CategoryRepository(db), new TransactionRepository(db));
            var data = await overview.ExecuteAsync(month);

            data.Should().NotBeNull();
            data!.TotalPlannedIncome.Grosze.Should().Be(747_000);

            // 350 000 + 200 000 + 160 000 — przelew własny NIE dolicza się do wypłaty.
            data.TotalActualIncome.Grosze.Should().Be(710_000);

            var wyplataRow = data.Incomes.Single(i => i.CategoryName == "Wypłata");
            wyplataRow.Planned.Grosze.Should().Be(387_000);
            wyplataRow.Actual.Grosze.Should().Be(350_000);
            wyplataRow.IsShort.Should().BeTrue();

            data.Incomes.Single(i => i.CategoryName == "Wpłata małżonka").Actual.Grosze.Should().Be(200_000);
            data.Incomes.Single(i => i.CategoryName == "800+").IsShort.Should().BeFalse();

            // Wszystkie aktywne źródła są na liście, także te puste — inaczej nie
            // widać, czego jeszcze nie zaplanowano.
            var inneRow = data.Incomes.Single(i => i.CategoryName == "Inne");
            inneRow.Planned.Grosze.Should().Be(0);
            inneRow.Actual.Grosze.Should().Be(0);

            // Puste źródła nie mogą rozbijać listy — trafiają na koniec.
            data.Incomes.Last().CategoryName.Should().Be("Inne");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Income_plan_survives_being_resaved()
    {
        // Ten sam wzorzec "usuń i odtwórz" co koperty — bez poprawki trackera EF
        // ponowny zapis planu rzucałby DbUpdateConcurrencyException.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_income2_{Guid.NewGuid():N}.db");
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
                var cat = Category.Create("Wypłata", CategoryKind.Income);
                db1.Categories.Add(cat);
                await db1.SaveChangesAsync();
                categoryId = cat.Id;

                var plan1 = new PlanMonthUseCase(new MonthBudgetRepository(db1), new UnitOfWork(db1));
                await plan1.ExecuteAsync(new PlanMonthUseCase.Input(
                    month, new Money(500_000), [],
                    [new PlanMonthUseCase.IncomeInput(cat.Id, new Money(387_000))]));
            }

            using var db2 = new CastellanDbContext(options);
            var plan2 = new PlanMonthUseCase(new MonthBudgetRepository(db2), new UnitOfWork(db2));

            var act = async () => await plan2.ExecuteAsync(new PlanMonthUseCase.Input(
                month, new Money(500_000), [],
                [new PlanMonthUseCase.IncomeInput(categoryId, new Money(400_000))]));

            await act.Should().NotThrowAsync();

            var reloaded = await new MonthBudgetRepository(db2).GetForMonthAsync(month);
            reloaded!.IncomePlans.Single().PlannedAmount.Grosze.Should().Be(400_000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
