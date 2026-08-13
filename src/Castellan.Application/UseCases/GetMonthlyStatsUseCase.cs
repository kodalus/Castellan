using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record MonthlyStat(YearMonth Month, Money Expense, Money Income)
{
    public Money Net => Income - Expense;

    public string MonthShort => new DateTime(Month.Year, Month.Month, 1)
        .ToString("MMM", CultureInfo.GetCultureInfo("pl-PL"));
}

public sealed record TopCategoryStat(string CategoryName, Money TotalSpent);

public sealed record MonthlyStats(
    IReadOnlyList<MonthlyStat> Months,
    IReadOnlyList<TopCategoryStat> TopExpenseCategories);

public sealed class GetMonthlyStatsUseCase(
    ITransactionRepository transactions,
    ICategoryRepository categories)
{
    public async Task<MonthlyStats> ExecuteAsync(
        YearMonth upTo, int monthCount = 6, CancellationToken ct = default)
    {
        // Build range [start .. upTo]
        var start = upTo;
        for (var i = 0; i < monthCount - 1; i++) start = start.Previous();

        var months = new List<MonthlyStat>(monthCount);
        var allExpenseTxs = new List<Transaction>();

        var current = start;
        while (current.CompareTo(upTo) <= 0)
        {
            var txs = await transactions.ListForMonthAsync(current, ct);
            var active = txs.Where(t => !t.IsExcludedFromCalculations).ToList();

            var expense = new Money(active.Where(t => t.Amount.IsNegative).Sum(t => t.Amount.Grosze));
            var income  = new Money(active.Where(t => !t.Amount.IsNegative).Sum(t => t.Amount.Grosze));

            months.Add(new MonthlyStat(current, expense.Abs(), income));
            allExpenseTxs.AddRange(active.Where(t => t.Amount.IsNegative));
            current = current.Next();
        }

        // Top 5 expense categories across the whole period
        var catIds = allExpenseTxs.Select(t => t.CategoryId).Distinct();
        var cats = await categories.GetManyAsync(catIds, ct);
        var catMap = cats.ToDictionary(c => c.Id);

        var topCats = allExpenseTxs
            .GroupBy(t => t.CategoryId)
            .Select(g => new TopCategoryStat(
                catMap.TryGetValue(g.Key, out var c) ? c.Name : "?",
                new Money(Math.Abs(g.Sum(t => t.Amount.Grosze)))))
            .OrderByDescending(x => x.TotalSpent.Grosze)
            .Take(5)
            .ToList();

        return new MonthlyStats(months, topCats);
    }
}
