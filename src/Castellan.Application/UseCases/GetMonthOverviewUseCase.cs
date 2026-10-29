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
    Money Remaining,
    /// <summary>
    /// Wydatek w kategorii, która nie ma koperty w tym miesiącu. Pieniądze wyszły
    /// tak samo jak każde inne, tylko plan o nich nie wiedział.
    /// </summary>
    bool IsUnplanned = false)
{
    // 0.0–1.0 capped (for ProgressBar.Progress)
    public double SpentRatio => Planned.Grosze == 0 ? 0.0
        : Math.Clamp((double)Math.Abs(Actual.Grosze) / (double)Planned.Grosze, 0.0, 1.0);

    // uncapped (> 1.0 when over budget — used for color)
    public double SpentFraction => Planned.Grosze == 0 ? 0.0
        : (double)Math.Abs(Actual.Grosze) / (double)Planned.Grosze;

    // Wydatek bez koperty jest przekroczeniem z definicji: plan na niego wynosi zero.
    public bool IsOverBudget => SpentFraction > 1.0 || (IsUnplanned && Actual.IsNegative);

    public string PlannedDisplay   => Planned.ToString();
    public string ActualDisplay    => Actual.Abs().ToString();
    public string RemainingDisplay => Remaining.ToString();

    public string ProgressLabel => IsUnplanned
        ? $"{ActualDisplay} — poza planem, bez koperty"
        : $"{ActualDisplay} z {PlannedDisplay}";

    /// <summary>Druga połowa podpisu kwoty na pulpicie, dopisywana po kwocie wydanej.</summary>
    public string TrailingLabel => IsUnplanned ? " — poza planem" : $" / {PlannedDisplay}";
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
    Money TotalActualIncome,
    /// <summary>Odłożone na rezerwę — osobno, bo to nie jest wydane.</summary>
    Money TotalSaved,
    /// <summary>Ile odłożono PONAD plan. Zero, gdy odłożono tyle albo mniej.</summary>
    Money SavedOverPlan,
    /// <summary>Wydatki w kategoriach, które nie mają koperty w tym miesiącu.</summary>
    IReadOnlyList<EnvelopeOverview> Unplanned,
    Money TotalUnplanned)
{
    public bool HasUnplanned => TotalUnplanned.Grosze > 0;

    /// <summary>
    /// Koperty plus wydatki bez koperty — do list na ekranach. Osobna właściwość, bo
    /// <see cref="Envelopes"/> ma pozostać dokładnie tym, co zaplanowano.
    /// </summary>
    public IReadOnlyList<EnvelopeOverview> EnvelopesWithUnplanned => [.. Envelopes, .. Unplanned];

    // 0.0–1.0 przycięte (dla ProgressBar.Progress)
    public double SpentRatio => TotalPlanned.Grosze == 0 ? 0.0
        : Math.Clamp((double)Math.Abs(TotalSpent.Grosze) / TotalPlanned.Grosze, 0.0, 1.0);

    // nieprzycięte — powyżej 1.0 znaczy przekroczony plan, stąd kolor
    public double SpentFraction => TotalPlanned.Grosze == 0 ? 0.0
        : (double)Math.Abs(TotalSpent.Grosze) / TotalPlanned.Grosze;

    public bool IsOverspent => RemainingToSpend.IsNegative;

    public bool HasSaved => TotalSaved.Grosze > 0;

    /// <summary>
    /// Plan przekroczony WYŁĄCZNIE przez odkładanie ponad plan — bez tej nadwyżki
    /// budżet by się spiął. To zupełnie inna sytuacja niż przepuszczenie pieniędzy:
    /// one nie zniknęły, tylko leżą na koncie oszczędnościowym i da się je wycofać.
    /// </summary>
    public bool IsOverspentBySaving =>
        IsOverspent && RemainingToSpend.Grosze + SavedOverPlan.Grosze >= 0;

    /// <summary>Ile zostałoby do wydania, gdyby odłożyć dokładnie tyle, ile w planie.</summary>
    public Money RemainingWithoutExtraSaving =>
        new(RemainingToSpend.Grosze + SavedOverPlan.Grosze);
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

        // "Wydano" / "Pozostało do wydania" liczone z transakcji miesiąca, nigdy z aktywów
        // ani funduszy — te pozostają poza budżetem miesiąca. Nie tylko z kopert: wydatki
        // w kategoriach bez koperty dochodzą niżej, bo te pieniądze też wyszły.
        // Odwrocony znak, nie wartosc bezwzgledna: koperta z nadwyzka zwrotow ma
        // faktyczna kwote DODATNIA, a modul zamienialby ten zwrot z powrotem w wydatek.
        //
        // Rezerwy liczą się do zużycia budżetu, ale NIE do „wydanych": odłożone
        // pieniądze przestają być do wydania w tym miesiącu, lecz nie zniknęły —
        // leżą na koncie oszczędnościowym. Nazywanie tego wydatkiem każe się bać
        // czegoś, co jest dokładnie odwrotnością szkody.
        var reserveId = cats.FirstOrDefault(c =>
            c.Name.Equals(ConfirmTransferUseCase.ReserveCategoryName, StringComparison.OrdinalIgnoreCase))?.Id;

        var reserve = reserveId is null
            ? null
            : envelopes.FirstOrDefault(e => e.CategoryId == reserveId);

        var totalSaved = new Money(reserve is null ? 0 : -reserve.Actual.Grosze);
        var totalSpent = new Money(envelopes
            .Where(e => reserve is null || e.CategoryId != reserve.CategoryId)
            .Sum(e => -e.Actual.Grosze));

        // Wydatek w kategorii bez koperty też jest wydatkiem. Dopóki „wydane” sumowało
        // same koperty, wystarczyło wyzerować kopertę — a wyzerowana znika z planu — żeby
        // razem z nią z pulpitu zniknęły wszystkie wydatki tej kategorii. Ekran pokazywał
        // wtedy MNIEJ wydane, niż było naprawdę, czyli mylił w stronę uspokajającą.
        //
        // Liczone są tylko kategorie na minusie: kategoria, w której jest sam zwrot,
        // nie jest wydatkiem i nie ma czego dopisywać do „wydane”.
        var planned = budget.Envelopes.Select(e => e.CategoryId).ToHashSet();
        var unplanned = spendingByCategory
            .Where(kv => !planned.Contains(kv.Key) && kv.Value.IsNegative)
            .Select(kv =>
            {
                catMap.TryGetValue(kv.Key, out var cat);
                return new EnvelopeOverview(
                    kv.Key,
                    cat?.Name ?? kv.Key.ToString(),
                    cat?.Kind ?? CategoryKind.Expense,
                    Money.Zero,
                    kv.Value,
                    kv.Value,
                    IsUnplanned: true);
            })
            // Przychód to nie wydatek, nawet gdy w danym miesiącu wyszedł na minus
            // (korekta, oddana nadpłata) — taka kwota należy do zestawienia przychodów.
            .Where(e => e.Kind != CategoryKind.Income)
            .OrderBy(e => e.Actual.Grosze)
            .ToList();

        var totalUnplanned = new Money(unplanned.Sum(e => -e.Actual.Grosze));
        totalSpent = new Money(totalSpent.Grosze + totalUnplanned.Grosze);

        var savedOverPlan = new Money(Math.Max(
            0, totalSaved.Grosze - (reserve?.Planned.Grosze ?? 0)));

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
            new Money(totalPlanned.Grosze - totalSpent.Grosze - totalSaved.Grosze),
            envelopes,
            incomes,
            budget.TotalPlannedIncome,
            new Money(incomes.Sum(i => i.Actual.Grosze)),
            totalSaved,
            savedOverPlan,
            unplanned,
            totalUnplanned);
    }
}
