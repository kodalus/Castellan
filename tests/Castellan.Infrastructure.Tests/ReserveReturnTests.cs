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
/// Wyjęcie odłożonych pieniędzy jest odwrotnością odkładania i musi dotykać tej samej
/// koperty.
///
/// Dotąd obie nogi wyjęcia wypadały z budżetu jak zwykły przelew — w obawie przed
/// policzeniem odpisu drugi raz. To mieszało dwie rzeczy: odpisu z MINIONEGO miesiąca
/// rzeczywiście nie wolno cofać wstecz, ale pieniądze wracają do wydawania w miesiącu,
/// w którym je wyjęto, i tam muszą być widoczne.
///
/// Skutek był taki, że koperta „Rezerwy” pokazywała samą sumę wpłat. U użytkowniczki
/// „19 000,00 zł odłożone na rezerwę” przy 20 500 wpłaconych i 9 170 wyjętych w tym
/// samym miesiącu — a budżet miesiąca był o całą tę różnicę zaniżony.
/// </summary>
public class ReserveReturnTests
{
    private const long Odlozone = 500_000;
    private const long Wyjete   = 200_000;

    [Fact]
    public async Task Taking_money_back_shrinks_the_reserve_envelope()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, rozliczeniowe, oszczednosciowe, fundusz) = await SetupAsync(dbPath);

            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                rozliczeniowe, oszczednosciowe, new Money(Odlozone), DateTimeOffset.Now,
                IsReserve: true, ContributeTo: fundusz));

            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                oszczednosciowe, rozliczeniowe, new Money(Wyjete), DateTimeOffset.Now,
                WithdrawFrom: fundusz, IsReserveReturn: true));

            var m = await Overview(db);

            var rezerwy = m!.Envelopes.Single(e => e.CategoryName == "Rezerwy");
            rezerwy.Actual.Grosze.Should().Be(-(Odlozone - Wyjete));
            m.TotalSaved.Grosze.Should().Be(Odlozone - Wyjete);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Money_taken_back_is_available_to_spend_again()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, rozliczeniowe, oszczednosciowe, fundusz) = await SetupAsync(dbPath);

            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                rozliczeniowe, oszczednosciowe, new Money(Odlozone), DateTimeOffset.Now,
                IsReserve: true, ContributeTo: fundusz));
            var poOdlozeniu = (await Overview(db))!.RemainingToSpend.Grosze;

            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                oszczednosciowe, rozliczeniowe, new Money(Wyjete), DateTimeOffset.Now,
                WithdrawFrom: fundusz, IsReserveReturn: true));

            // Te pieniądze leżą znowu na koncie do wydawania, więc budżet miesiąca
            // musi o nie urosnąć — inaczej ekran straszy brakiem, którego nie ma.
            (await Overview(db))!.RemainingToSpend.Grosze.Should().Be(poOdlozeniu + Wyjete);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Plain_moving_between_accounts_still_leaves_the_budget_alone()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, rozliczeniowe, oszczednosciowe, _) = await SetupAsync(dbPath);
            var przed = (await Overview(db))!.RemainingToSpend.Grosze;

            // Kontrola negatywna: „przekładam" to nadal przelew niewidoczny dla budżetu.
            // Bez tego każdy ruch z oszczędnościowego podnosiłby kwotę do wydania.
            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                oszczednosciowe, rozliczeniowe, new Money(Wyjete), DateTimeOffset.Now));

            var m = await Overview(db);
            m!.RemainingToSpend.Grosze.Should().Be(przed);
            m.TotalSaved.Grosze.Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Taking_back_is_not_counted_as_income()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, rozliczeniowe, oszczednosciowe, fundusz) = await SetupAsync(dbPath);

            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                rozliczeniowe, oszczednosciowe, new Money(Odlozone), DateTimeOffset.Now,
                IsReserve: true, ContributeTo: fundusz));
            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                oszczednosciowe, rozliczeniowe, new Money(Wyjete), DateTimeOffset.Now,
                WithdrawFrom: fundusz, IsReserveReturn: true));

            // Wpis z kwotą dodatnią na koncie rozliczeniowym kusi, żeby policzyć go
            // jako przychód. To nie jest przychód: te pieniądze już raz wpłynęły.
            (await Overview(db))!.TotalActualIncome.Grosze.Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_fund_balance_follows_in_both_directions()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, rozliczeniowe, oszczednosciowe, fundusz) = await SetupAsync(dbPath);

            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                rozliczeniowe, oszczednosciowe, new Money(Odlozone), DateTimeOffset.Now,
                IsReserve: true, ContributeTo: fundusz));
            await Transfer(db).ExecuteAsync(new CreateTransferUseCase.Input(
                oszczednosciowe, rozliczeniowe, new Money(Wyjete), DateTimeOffset.Now,
                WithdrawFrom: fundusz, IsReserveReturn: true));

            db.ChangeTracker.Clear();
            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(Odlozone - Wyjete);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static CreateTransferUseCase Transfer(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        return new CreateTransferUseCase(
            new AccountRepository(db), new TransactionRepository(db),
            new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db));
    }

    private static Task<MonthOverview?> Overview(CastellanDbContext db)
    {
        db.ChangeTracker.Clear();
        return new GetMonthOverviewUseCase(
            new MonthBudgetRepository(db), new CategoryRepository(db), new TransactionRepository(db))
            .ExecuteAsync(YearMonth.Current);
    }

    private static async Task<(CastellanDbContext db, AccountId rozliczeniowe, AccountId oszczednosciowe, FundId fundusz)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var kiedys = DateTimeOffset.UtcNow.AddYears(-1);
        var ing = Account.Create("ING osobisty", AccountKind.Checking, new Money(2_000_000), kiedys, "ING");
        var oko = Account.Create("OKO ING", AccountKind.Savings, new Money(1_000_000), kiedys, "ING");
        var rezerwy = Category.Create(ConfirmTransferUseCase.ReserveCategoryName, CategoryKind.Expense);
        var fundusz = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_000_000), null);

        db.Accounts.AddRange(ing, oko);
        db.Categories.Add(rezerwy);
        db.Funds.Add(fundusz);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new PlanMonthUseCase(new MonthBudgetRepository(db), new UnitOfWork(db)).ExecuteAsync(
            new PlanMonthUseCase.Input(
                YearMonth.Current, new Money(2_000_000),
                [new PlanMonthUseCase.EnvelopeInput(rezerwy.Id, new Money(Odlozone))], []));
        db.ChangeTracker.Clear();

        return (db, ing.Id, oko.Id, fundusz.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_rezerwa_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
