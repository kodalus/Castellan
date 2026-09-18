using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record AssetRow(
    AssetId Id,
    string Name,
    AssetLiquidity Liquidity,
    Money Value,
    DateOnly UpdatedOn,
    bool IsAccount = false);

public sealed record CushionTier(
    AssetLiquidity Liquidity,
    string LiquidityDisplay,
    Money TierValue,
    Money CumulativeValue,
    double MonthsTier,
    double MonthsCumulative,
    IReadOnlyList<AssetRow> Assets);

public sealed record CushionOverview(
    IReadOnlyList<CushionTier> Tiers,
    Money AvgMonthlyExpense,
    int MonthsOfData,
    double TotalMonths,
    Money TotalValue);

public sealed class GetCushionOverviewUseCase(
    IAssetRepository assets,
    ICategoryRepository categories,
    ITransactionRepository transactions,
    GetAccountsWithBalancesUseCase accountBalances)
{
    private static readonly AssetLiquidity[] TierOrder =
        [AssetLiquidity.Immediate, AssetLiquidity.Fast, AssetLiquidity.Medium, AssetLiquidity.Slow];

    public async Task<CushionOverview> ExecuteAsync(int expenseMonths = 3, CancellationToken ct = default)
    {
        var allAssets = await assets.ListAsync(ct);
        var active = allAssets.Where(a => !a.IsArchived).ToList();

        var (avgExpense, monthsUsed) = await ComputeAvgExpenseAsync(expenseMonths, ct);

        // Salda WSZYSTKICH kont liczą się do płynności natychmiastowej — także
        // oszczędnościowych. Wcześniej brane były tylko rozliczeniowe, więc pieniądze
        // z konta oszczędnościowego nie pojawiały się w Majątku w ogóle: ani w poduszce,
        // ani w wartości netto. Dla ekranu, który ma odpowiadać na pytanie „ile mam",
        // pomijanie realnych pieniędzy jest gorsze niż ryzyko, że ktoś doda to samo konto
        // drugi raz jako aktywo.
        //
        // Poziom natychmiastowy, bo przelew z konta oszczędnościowego na własne konto
        // rozliczeniowe jest w praktyce natychmiastowy.
        //
        // Salda FUNDUSZY celowo nie wchodzą tu wcale. Fundusz jest tylko kopertą nad
        // pieniędzmi leżącymi na którymś z tych kont, więc doliczenie go liczyłoby tę
        // samą złotówkę dwa razy. Pieniądze poza kontami znanymi aplikacji dodaje się
        // jako aktywo — i wtedy są tu poniżej, razem z pozostałymi aktywami.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var accountRows = (await accountBalances.ExecuteAsync(ct))
            .Where(a => !a.IsArchived)
            .Select(a => new AssetRow(
                default, $"Konto: {a.Name}", AssetLiquidity.Immediate,
                a.CurrentBalance, today, IsAccount: true))
            .ToList();

        var cumulative = 0L;
        var tiers = new List<CushionTier>();

        foreach (var liquidity in TierOrder)
        {
            var tierAssets = active
                .Where(a => a.Liquidity == liquidity)
                .Select(a => new AssetRow(a.Id, a.Name, a.Liquidity, a.Value, a.UpdatedOn))
                .ToList();

            if (liquidity == AssetLiquidity.Immediate)
                tierAssets = [.. accountRows, .. tierAssets];

            var tierValue = tierAssets.Sum(a => a.Value.Grosze);
            cumulative += tierValue;

            double monthsTier  = avgExpense.Grosze > 0 ? (double)tierValue   / avgExpense.Grosze : 0;
            double monthsCumul = avgExpense.Grosze > 0 ? (double)cumulative  / avgExpense.Grosze : 0;

            tiers.Add(new CushionTier(
                liquidity,
                LiquidityDisplay(liquidity),
                new Money(tierValue),
                new Money(cumulative),
                monthsTier,
                monthsCumul,
                tierAssets));
        }

        double totalMonths = avgExpense.Grosze > 0
            ? (double)cumulative / avgExpense.Grosze
            : 0;

        return new CushionOverview(tiers, avgExpense, monthsUsed, totalMonths, new Money(cumulative));
    }

    /// <summary>
    /// Średnia liczy WYDATKI NA ŻYCIE, więc pomija kategorię „Rezerwy". Odkładanie na
    /// bok nie jest kosztem utrzymania — w miesiącu bez przychodu przestaje się odkładać
    /// pierwsze. Gdyby wchodziło do średniej, każda złotówka odłożona na fundusz
    /// SKRACAŁABY liczbę miesięcy, które ta sama złotówka wydłuża.
    /// </summary>
    private async Task<(Money avg, int months)> ComputeAvgExpenseAsync(int count, CancellationToken ct)
    {
        var allCategories = await categories.ListAsync(ct);
        var catMap = allCategories.ToDictionary(c => c.Id);
        var reserveId = allCategories
            .FirstOrDefault(c => c.Name.Equals(ConfirmTransferUseCase.ReserveCategoryName,
                StringComparison.OrdinalIgnoreCase))?.Id;

        var today   = DateOnly.FromDateTime(DateTime.Today);
        var upTo    = new YearMonth(today.Year, today.Month);
        var from    = upTo;
        for (var i = 0; i < count - 1; i++) from = from.Previous();

        var total      = 0L;
        var usedMonths = 0;
        var current    = from;
        while (current.CompareTo(upTo) <= 0)
        {
            var txs = await transactions.ListForMonthAsync(current, ct);
            // Zwrot kosztow pomniejsza tu wydatki tego miesiaca, bo tyle naprawde
            // kosztowalo zycie: oddany towar nie jest kosztem utrzymania.
            var expenses = txs
                .Where(t => !t.IsExcludedFromCalculations
                         && (reserveId is null || t.CategoryId != reserveId))
                .Sum(t => RefundAccounting.NetExpenseGrosze(t, catMap));
            if (expenses > 0) { total += expenses; usedMonths++; }
            current = current.Next();
        }

        if (usedMonths == 0) return (Money.Zero, 0);
        return (new Money(total / usedMonths), usedMonths);
    }

    internal static string LiquidityDisplay(AssetLiquidity liquidity) => liquidity switch
    {
        AssetLiquidity.Immediate => "Natychmiastowa",
        AssetLiquidity.Fast      => "Szybka (1–3 dni)",
        AssetLiquidity.Medium    => "Średnia (tygodnie)",
        AssetLiquidity.Slow      => "Wolna (miesiące)",
        _                        => "?",
    };
}
