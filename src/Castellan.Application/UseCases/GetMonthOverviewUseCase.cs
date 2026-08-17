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
    // 0.0–1.0 capped (for ProgressBar.Progress)
    public double SpentRatio => Planned.Grosze == 0 ? 0.0
        : Math.Clamp((double)Math.Abs(Actual.Grosze) / (double)Planned.Grosze, 0.0, 1.0);

    // uncapped (> 1.0 when over budget — used for color)
    public double SpentFraction => Planned.Grosze == 0 ? 0.0
        : (double)Math.Abs(Actual.Grosze) / (double)Planned.Grosze;

    public bool IsOverBudget => SpentFraction > 1.0;

    public string PlannedDisplay   => Planned.ToString();
    public string ActualDisplay    => Actual.Abs().ToString();
    public string RemainingDisplay => Remaining.ToString();
    public string ProgressLabel    => $"{ActualDisplay} z {PlannedDisplay}";
}

/// <summary>Planowany kontra faktyczny przychód z jednego źródła.</summary>
public sealed record IncomeOverview(
    CategoryId CategoryId,
    string CategoryName,
    Money Planned,
    Money Actual)
{
    public Money Difference => Actual - Planned;

    public double ReceivedRatio => Planned.Grosze == 0 ? 0.0
        : Math.Clamp((double)Actual.Grosze / Planned.Grosze, 0.0, 1.0);

    public bool IsShort => Actual < Planned;

    public string PlannedDisplay => Planned.ToString();
    public string ActualDisplay  => Actual.ToString();
    public string ProgressLabel  => $"{ActualDisplay} z {PlannedDisplay}";
}

public sealed record MonthOverview(
    YearMonth Month,
    Money AvailableFunds,
    Money TotalPlanned,
    Money RemainingToAllocate,
    Money TotalSpent,
    Money RemainingToSpend,
    IReadOnlyList<EnvelopeOverview> Envelopes,
    IReadOnlyList<IncomeOverview> Incomes,
    Money TotalPlannedIncome,
    Money TotalActualIncome)
{
    // 0.0–1.0 przycięte (dla ProgressBar.Progress)
    public double SpentRatio => TotalPlanned.Grosze == 0 ? 0.0
        : Math.Clamp((double)Math.Abs(TotalSpent.Grosze) / TotalPlanned.Grosze, 0.0, 1.0);

    // nieprzycięte — powyżej 1.0 znaczy przekroczony plan, stąd kolor
    public double SpentFraction => TotalPlanned.Grosze == 0 ? 0.0
        : (double)Math.Abs(TotalSpent.Grosze) / TotalPlanned.Grosze;

    public bool IsOverspent => RemainingToSpend.IsNegative;
}

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

        // "Wydano" / "Pozostało do wydania" liczone wyłącznie z planu (kopert),
        // nigdy z aktywów ani funduszy — te pozostają poza budżetem miesiąca.
        var totalSpent = new Money(envelopes.Sum(e => Math.Abs(e.Actual.Grosze)));

        // Faktyczne wpływy: dodatnie, nieodrzucone transakcje — ta sama definicja
        // co w statystykach. Transfery między własnymi kontami są wykluczone przez
        // IsExcludedFromCalculations, więc przerzucenie 2000 zł z wypłaty na wspólne
        // konto nie zawyży przychodów.
        var incomeByCategory = monthTxs
            .Where(t => !t.IsExcludedFromCalculations && !t.Amount.IsNegative)
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => new Money(g.Sum(t => t.Amount.Grosze)));

        // Wszystkie aktywne źródła przychodu — także te bez planu i bez wpływów,
        // żeby zestawienie od razu pokazywało, czego jeszcze nie zaplanowano.
        // Do tego zarchiwizowane lub zaplanowane wcześniej źródła, jeśli coś z nich
        // wpłynęło — inaczej realny przychód zniknąłby z widoku.
        var incomeCategoryIds = cats
            .Where(c => !c.IsSystem && !c.IsArchived && c.Kind == CategoryKind.Income)
            .Select(c => c.Id)
            .Union(budget.IncomePlans.Select(p => p.CategoryId))
            .Union(incomeByCategory.Keys.Where(id =>
                catMap.TryGetValue(id, out var c) && c.Kind == CategoryKind.Income))
            .ToList();

        var incomes = incomeCategoryIds
            .Select(id =>
            {
                catMap.TryGetValue(id, out var cat);
                var planned = budget.IncomePlans.FirstOrDefault(p => p.CategoryId == id)?.PlannedAmount ?? Money.Zero;
                return new IncomeOverview(
                    id,
                    cat?.Name ?? id.ToString(),
                    planned,
                    incomeByCategory.GetValueOrDefault(id, Money.Zero));
            })
            // Najpierw zaplanowane (od największych), potem nieplanowane wpływy,
            // a puste źródła alfabetycznie na końcu — żeby nie rozbijały listy.
            .OrderByDescending(i => i.Planned.Grosze)
            .ThenByDescending(i => i.Actual.Grosze)
            .ThenBy(i => i.CategoryName)
            .ToList();

        return new MonthOverview(
            month,
            budget.AvailableFunds,
            totalPlanned,
            budget.AvailableFunds - totalPlanned,
            totalSpent,
            totalPlanned - totalSpent,
            envelopes,
            incomes,
            budget.TotalPlannedIncome,
            new Money(incomes.Sum(i => i.Actual.Grosze)));
    }
}
