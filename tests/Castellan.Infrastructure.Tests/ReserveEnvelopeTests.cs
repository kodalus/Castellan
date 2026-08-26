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
/// Odłożenie pieniędzy na własne konto oszczędnościowe wygląda dla aplikacji jak
/// zwykły przelew między kontami, a dla budżetu jest czymś przeciwnym: te pieniądze
/// przestają być do wydania w tym miesiącu. Dopóki obie nogi wypadały z wyliczeń,
/// koperta „Rezerwy" pokazywała zero wydanych niezależnie od tego, ile realnie poszło.
/// </summary>
public class ReserveEnvelopeTests
{
    private sealed record Env(
        CastellanDbContext Db,
        ConfirmTransferUseCase Confirm,
        GetMonthOverviewUseCase Overview,
        GetCushionOverviewUseCase Cushion,
        ContributeToFundUseCase Contribute,
        Account Checking,
        Account Savings,
        Category Reserve);

    private static async Task<Env> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var checking = Account.Create("ING", AccountKind.Checking, new Money(1_000_000), DateTimeOffset.UtcNow.AddYears(-1));
        var savings = Account.Create("Oszczednosciowe", AccountKind.Savings, Money.Zero, DateTimeOffset.UtcNow.AddYears(-1));
        var reserve = Category.Create("Rezerwy", CategoryKind.Expense);
        var food = Category.Create("Produkty do domu", CategoryKind.Expense);
        db.Accounts.AddRange(checking, savings);
        db.Categories.AddRange(reserve, food);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new YearMonth(today.Year, today.Month);
        var budget = MonthBudget.Create(month, new Money(500_000));
        budget.Plan(reserve.Id, new Money(50_000));
        budget.Plan(food.Id, new Money(100_000));
        db.MonthBudgets.Add(budget);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var txRepo = new TransactionRepository(db);
        var catRepo = new CategoryRepository(db);
        var fundRepo = new FundRepository(db);
        var uow = new UnitOfWork(db);

        return new Env(
            db,
            new ConfirmTransferUseCase(txRepo, catRepo, fundRepo, uow),
            new GetMonthOverviewUseCase(new MonthBudgetRepository(db), catRepo, txRepo),
            new GetCushionOverviewUseCase(
                new AssetRepository(db), catRepo, txRepo,
                new GetAccountsWithBalancesUseCase(new AccountRepository(db), txRepo)),
            new ContributeToFundUseCase(fundRepo, uow),
            checking, savings, reserve);
    }

    /// <summary>Para nóg przelewu, tak jak proponuje ją skrzynka.</summary>
    private static async Task<Guid> ProposeTransferAsync(Env env, long grosze)
    {
        var groupId = Guid.NewGuid();
        var out_ = Transaction.CreateManual(env.Checking.Id, new Money(-grosze), DateTimeOffset.Now, Category.UnsortedId);
        var in_ = Transaction.CreateManual(env.Savings.Id, new Money(grosze), DateTimeOffset.Now, Category.UnsortedId);
        out_.ProposeTransfer(groupId);
        in_.ProposeTransfer(groupId);
        env.Db.Transactions.AddRange(out_, in_);
        await env.Db.SaveChangesAsync();
        env.Db.ChangeTracker.Clear();
        return groupId;
    }

    private static async Task<Money> ReserveSpentAsync(Env env)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var overview = await env.Overview.ExecuteAsync(new YearMonth(today.Year, today.Month));
        return overview!.Envelopes.Single(e => e.CategoryName == "Rezerwy").Actual;
    }

    [Fact]
    public async Task Saving_to_your_own_savings_account_charges_the_reserve_envelope()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_res_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var groupId = await ProposeTransferAsync(env, 40_000);

            (await ReserveSpentAsync(env)).Grosze.Should().Be(0, "przed zatwierdzeniem nic nie jest rozstrzygnięte");

            await env.Confirm.ExecuteAsReserveAsync(groupId, contributeTo: null);
            env.Db.ChangeTracker.Clear();

            (await ReserveSpentAsync(env)).Grosze.Should().Be(-40_000);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_incoming_leg_never_shows_up_as_income()
    {
        // Gdyby noga przychodząca też przestała być przelewem, te same 400 zł
        // wyszłyby jako przychód na koncie oszczędnościowym.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_res_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var groupId = await ProposeTransferAsync(env, 40_000);

            await env.Confirm.ExecuteAsReserveAsync(groupId, contributeTo: null);
            env.Db.ChangeTracker.Clear();

            var incoming = await env.Db.Transactions.SingleAsync(t => t.AccountId == env.Savings.Id);
            incoming.IsExcludedFromCalculations.Should().BeTrue();

            var outgoing = await env.Db.Transactions.SingleAsync(t => t.AccountId == env.Checking.Id);
            outgoing.IsExcludedFromCalculations.Should().BeFalse();
            outgoing.CategoryId.Should().Be(env.Reserve.Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Choosing_a_fund_raises_its_balance_by_the_transferred_amount()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_res_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);

            var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(8));
            env.Db.Funds.Add(fund);
            await env.Db.SaveChangesAsync();
            env.Db.ChangeTracker.Clear();

            var groupId = await ProposeTransferAsync(env, 40_000);
            await env.Confirm.ExecuteAsReserveAsync(groupId, fund.Id);
            env.Db.ChangeTracker.Clear();

            (await env.Db.Funds.SingleAsync()).Balance.Grosze.Should().Be(40_000);
            (await ReserveSpentAsync(env)).Grosze.Should().Be(-40_000, "jedna czynność, jedno obciążenie koperty");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task An_ordinary_transfer_still_disappears_from_the_budget()
    {
        // Kontrola: zmiana dotyczy TYLKO drogi „odkładam". Zwykłe przekładanie
        // pieniędzy musi dalej wypadać z budżetu obiema nogami.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_res_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);
            var groupId = await ProposeTransferAsync(env, 40_000);

            await env.Confirm.ExecuteAsync(groupId);
            env.Db.ChangeTracker.Clear();

            (await env.Db.Transactions.ToListAsync())
                .Should().OnlyContain(t => t.IsExcludedFromCalculations);
            (await ReserveSpentAsync(env)).Grosze.Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Money_set_aside_does_not_count_as_a_living_cost()
    {
        // Poduszka dzieli aktywa przez średnie wydatki. Gdyby odkładanie liczyło się
        // jako wydatek, każda odłożona złotówka SKRACAŁA liczbę miesięcy, które ta sama
        // złotówka wydłuża — im pilniej oszczędzasz, tym gorszy wynik.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_res_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);

            // 1 000 zł realnych wydatków na życie w tym miesiącu.
            var food = await env.Db.Categories.SingleAsync(c => c.Name == "Produkty do domu");
            env.Db.Transactions.Add(Transaction.CreateManual(
                env.Checking.Id, new Money(-100_000), DateTimeOffset.Now, food.Id));
            await env.Db.SaveChangesAsync();
            env.Db.ChangeTracker.Clear();

            var before = (await env.Cushion.ExecuteAsync()).AvgMonthlyExpense.Grosze;

            var groupId = await ProposeTransferAsync(env, 40_000);
            await env.Confirm.ExecuteAsReserveAsync(groupId, contributeTo: null);
            env.Db.ChangeTracker.Clear();

            var after = (await env.Cushion.ExecuteAsync()).AvgMonthlyExpense.Grosze;
            after.Should().Be(before, "odkładanie na bok to nie koszt utrzymania");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Raising_a_fund_balance_alone_never_touches_any_account()
    {
        // ContributeToFundUseCase podnosi WYŁĄCZNIE saldo funduszu. Był tu kiedyś wariant
        // z kontem, który dopisywał wydatek w „Rezerwach" — i zapisywał połowę zdarzenia:
        // ile ubyło ze źródła, bez tego, gdzie pieniądze wylądowały. Suma majątku spadała,
        // mimo że pieniądze nadal były. Ruch pieniędzy idzie teraz przelewem, który ma
        // obie strony.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_res_{Guid.NewGuid():N}.db");
        try
        {
            var env = await SetupAsync(dbPath);

            var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(8));
            env.Db.Funds.Add(fund);
            await env.Db.SaveChangesAsync();
            env.Db.ChangeTracker.Clear();

            await env.Contribute.ExecuteAsync(fund.Id, new Money(30_000));
            env.Db.ChangeTracker.Clear();

            (await env.Db.Funds.SingleAsync()).Balance.Grosze.Should().Be(30_000);
            (await env.Db.Transactions.CountAsync()).Should().Be(0, "żaden ruch na koncie się nie odbył");
            (await ReserveSpentAsync(env)).Grosze.Should().Be(0, "koperty obciąża dopiero przelew");
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
