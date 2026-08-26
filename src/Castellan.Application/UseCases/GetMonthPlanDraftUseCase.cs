using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Co pokazać na ekranie planowania: zapisany plan miesiąca, a gdy go nie ma — plan
/// poprzedniego miesiąca jako punkt wyjścia.
/// </summary>
public sealed record MonthPlanDraft(MonthBudget? Budget, MonthBudget? Template)
{
    public bool IsCarriedOver => Budget is null && Template is not null;
}

public sealed class GetMonthPlanDraftUseCase(IMonthBudgetRepository budgets)
{
    public async Task<MonthPlanDraft> ExecuteAsync(YearMonth month, CancellationToken ct = default)
    {
        var budget = await budgets.GetForMonthAsync(month, ct);

        // Poprzedni miesiąc TYLKO wtedy, gdy planu nie ma. Wydatki stałe powtarzają się
        // co miesiąc, a przepisywanie kilkunastu kopert od zera to praca, którą aplikacja
        // ma odbierać, nie zadawać.
        //
        // Zaplanowany miesiąc zostaje nietknięty — inaczej koperta świadomie ustawiona
        // na zero wracałaby z poprzednią kwotą przy każdym wejściu na ekran, po cichu
        // nadpisując decyzję.
        var template = budget is null ? await budgets.GetForMonthAsync(month.Previous(), ct) : null;

        return new MonthPlanDraft(budget, template);
    }
}
