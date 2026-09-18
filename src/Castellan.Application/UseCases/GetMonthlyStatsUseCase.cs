using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
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

        // Pelna mapa kategorii, nie tylko tych z wydatkow: rozpoznanie zwrotu kosztow
        // wymaga rodzaju kategorii, a zwrot ma kwote DODATNIA — po starym filtrze po
        // znaku nigdy by tu nie trafil.
        var catMap = (await categories.ListAsync(ct)).ToDictionary(c => c.Id);

        var months = new List<MonthlyStat>(monthCount);
        var allExpenseTxs = new List<Transaction>();

        var current = start;
        while (current.CompareTo(upTo) <= 0)
        {
            var txs = await transactions.ListForMonthAsync(current, ct);
            var active = txs.Where(t => !t.IsExcludedFromCalculations).ToList();

            // Zwrot kosztow zbija wydatki, zamiast zawyzac przychody. Inaczej ten sam
            // zwrot pomniejszalby koperte w Planie i JEDNOCZESNIE rosl jako przychod
            // w Statystykach — dwie liczby o tym samym zdarzeniu, mowiace co innego.
            var expense = active.Sum(t => RefundAccounting.NetExpenseGrosze(t, catMap));
            var income  = active.Where(t => RefundAccounting.IsIncome(t, catMap)).Sum(t => t.Amount.Grosze);

            months.Add(new MonthlyStat(current, new Money(expense), new Money(income)));
            allExpenseTxs.AddRange(active.Where(t =>
                t.Amount.IsNegative || RefundAccounting.IsRefund(t, catMap)));
            current = current.Next();
        }

        var topCats = allExpenseTxs
            .GroupBy(t => t.CategoryId)
            .Select(g => new TopCategoryStat(
                catMap.TryGetValue(g.Key, out var c) ? c.Name : "?",
                new Money(g.Sum(t => RefundAccounting.NetExpenseGrosze(t, catMap)))))
            .OrderByDescending(x => x.TotalSpent.Grosze)
            .Take(5)
            .ToList();

        return new MonthlyStats(months, topCats);
    }
}
