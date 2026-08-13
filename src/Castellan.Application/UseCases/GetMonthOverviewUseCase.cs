using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record EnvelopeOverview(
    CategoryId CategoryId,
    string CategoryName,
    CategoryKind Kind,
    Money Planned,
    Money Actual,
    Money Remaining)
{
    // 0.0–1.0 share of planned amount already spent (absolute value, capped at 1)
    public double SpentRatio => Planned.Grosze == 0 ? 0.0
        : Math.Clamp((double)Math.Abs(Actual.Grosze) / (double)Planned.Grosze, 0.0, 1.0);

    public string PlannedDisplay   => Planned.ToString();
    public string ActualDisplay    => Actual.Abs().ToString();
    public string RemainingDisplay => Remaining.ToString();
}

public sealed record MonthOverview(
    YearMonth Month,
    Money AvailableFunds,
    Money TotalPlanned,
    Money RemainingToAllocate,
    IReadOnlyList<EnvelopeOverview> Envelopes);

public sealed class GetMonthOverviewUseCase(
    IMonthBudgetRepository budgets,
    ICategoryRepository categories,
    ITransactionRepository transactions)
{
    public async Task<MonthOverview?> ExecuteAsync(YearMonth month, CancellationToken ct = default)
    {
        var budget = await budgets.GetForMonthAsync(month, ct);
        if (budget is null) return null;

        var monthTxs = await transactions.ListForMonthAsync(month, ct);
        var spendingByCategory = monthTxs
            .Where(t => !t.IsExcludedFromCalculations)
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => new Money(g.Sum(t => t.Amount.Grosze)));

        var cats = await categories.ListAsync(ct);
        var catMap = cats.ToDictionary(c => c.Id);

        var envelopes = budget.Envelopes
            .Select(e =>
            {
                var actual = spendingByCategory.GetValueOrDefault(e.CategoryId, Money.Zero);
                catMap.TryGetValue(e.CategoryId, out var cat);
                return new EnvelopeOverview(
                    e.CategoryId,
                    cat?.Name ?? e.CategoryId.ToString(),
                    cat?.Kind ?? CategoryKind.Expense,
                    e.PlannedAmount,
                    actual,
                    e.PlannedAmount + actual);
            })
            .ToList();

        var totalPlanned = budget.Envelopes.Select(e => e.PlannedAmount).Sum();

        return new MonthOverview(
            month,
            budget.AvailableFunds,
            totalPlanned,
            budget.AvailableFunds - totalPlanned,
            envelopes);
    }
}
