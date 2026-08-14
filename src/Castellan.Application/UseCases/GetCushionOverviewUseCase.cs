using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record AssetRow(
    AssetId Id,
    string Name,
    AssetLiquidity Liquidity,
    Money Value,
    DateOnly UpdatedOn);

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
    double TotalMonths);

public sealed class GetCushionOverviewUseCase(
    IAssetRepository assets,
    ITransactionRepository transactions)
{
    private static readonly AssetLiquidity[] TierOrder =
        [AssetLiquidity.Immediate, AssetLiquidity.Fast, AssetLiquidity.Medium, AssetLiquidity.Slow];

    public async Task<CushionOverview> ExecuteAsync(int expenseMonths = 3, CancellationToken ct = default)
    {
        var allAssets = await assets.ListAsync(ct);
        var active = allAssets.Where(a => !a.IsArchived).ToList();

        var (avgExpense, monthsUsed) = await ComputeAvgExpenseAsync(expenseMonths, ct);

        var cumulative = 0L;
        var tiers = new List<CushionTier>();

        foreach (var liquidity in TierOrder)
        {
            var tierAssets = active
                .Where(a => a.Liquidity == liquidity)
                .Select(a => new AssetRow(a.Id, a.Name, a.Liquidity, a.Value, a.UpdatedOn))
                .ToList();

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
            ? (double)active.Sum(a => a.Value.Grosze) / avgExpense.Grosze
            : 0;

        return new CushionOverview(tiers, avgExpense, monthsUsed, totalMonths);
    }

    private async Task<(Money avg, int months)> ComputeAvgExpenseAsync(int count, CancellationToken ct)
    {
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
            var expenses = txs
                .Where(t => !t.IsExcludedFromCalculations && t.Amount.IsNegative)
                .Sum(t => Math.Abs(t.Amount.Grosze));
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
