using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <param name="Expense">Samo wydawanie — bez odłożonego na rezerwę.</param>
/// <param name="Saved">
/// Odłożone NETTO: wpłaty na rezerwę minus to, co z niej w tym miesiącu wyjęto.
/// Sama suma wpłat kłamałaby tym mocniej, im częściej sięgasz do odłożonych pieniędzy.
/// </param>
public sealed record MonthlyStat(YearMonth Month, Money Expense, Money Income, Money Saved)
{
    /// <summary>
    /// Netto odejmuje OBIE kwoty, bo ma się zgadzać z tym, co widać na kontach.
    /// Odłożenie nie jest wydatkiem, ale jest prawdziwym ubytkiem z miesiąca — pominięcie
    /// go pokazywałoby nadwyżkę w miesiącu, w którym konto realnie zjechało w dół.
    /// </summary>
    public Money Net => Income - Expense - Saved;

    public string MonthShort => new DateTime(Month.Year, Month.Month, 1)
        .ToString("MMM", CultureInfo.GetCultureInfo("pl-PL"));
}

public sealed record TopCategoryStat(string CategoryName, Money TotalSpent);

public sealed record MonthlyStats(
    IReadOnlyList<MonthlyStat> Months,
    IReadOnlyList<TopCategoryStat> TopExpenseCategories)
{
    public Money TotalSaved => new(Months.Sum(m => m.Saved.Grosze));

    public bool HasSaved => TotalSaved.Grosze != 0;
}

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
        var allCategories = await categories.ListAsync(ct);
        var catMap = allCategories.ToDictionary(c => c.Id);

        // Odłożenie na rezerwę nie jest wydatkiem — pieniądze nie zostały skonsumowane,
        // tylko przeniesione. Liczone razem z zakupami czyniły „wydatki” miarą bez sensu:
        // miesiąc, w którym odłożono 19 000 zł, wyglądał na katastrofę wydatkową, a „Rezerwy”
        // stały na pierwszym miejscu listy „na co wydaję” i wypychały z niej prawdziwe
        // kategorie. Poduszka wyklucza je od dawna, Pulpit pokazuje osobno — Statystyki
        // były tu jedynym miejscem, które wrzucało jedno i drugie do wspólnego worka.
        var reserveId = allCategories
            .FirstOrDefault(c => c.Name.Equals(ConfirmTransferUseCase.ReserveCategoryName,
                StringComparison.OrdinalIgnoreCase))?.Id;

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
            var rezerwowe = reserveId is null
                ? []
                : active.Where(t => t.CategoryId == reserveId).ToList();
            var pozostale = active.Except(rezerwowe).ToList();

            var expense = pozostale.Sum(t => RefundAccounting.NetExpenseGrosze(t, catMap));
            var income  = pozostale.Where(t => RefundAccounting.IsIncome(t, catMap)).Sum(t => t.Amount.Grosze);

            // Znak odwrócony, nie wartość bezwzględna: wpłata na rezerwę ma kwotę ujemną,
            // wyjęcie dodatnią, więc suma sama daje odłożone netto.
            var saved = -rezerwowe.Sum(t => t.Amount.Grosze);

            months.Add(new MonthlyStat(current, new Money(expense), new Money(income), new Money(saved)));
            allExpenseTxs.AddRange(pozostale.Where(t =>
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
