using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class PlanMonthUseCase(
    IMonthBudgetRepository budgets,
    IUnitOfWork uow)
{
    public sealed record EnvelopeInput(CategoryId CategoryId, Money PlannedAmount);
    public sealed record IncomeInput(CategoryId CategoryId, Money PlannedAmount);

    public sealed record Input(
        YearMonth Month,
        Money AvailableFunds,
        IReadOnlyList<EnvelopeInput> Envelopes,
        IReadOnlyList<IncomeInput>? Incomes = null);

    public async Task<MonthBudgetId> ExecuteAsync(Input input, CancellationToken ct = default)
    {
        var budget = await budgets.GetForMonthAsync(input.Month, ct);

        if (budget is null)
        {
            budget = MonthBudget.Create(input.Month, input.AvailableFunds);
            await budgets.AddAsync(budget, ct);
        }
        else
        {
            budget.RefreshAvailableFunds(input.AvailableFunds);
            foreach (var e in budget.Envelopes.ToList())
                budget.Remove(e.CategoryId);
            foreach (var p in budget.IncomePlans.ToList())
                budget.RemoveIncome(p.CategoryId);
        }

        // N-1 enforced by MonthBudget.Plan()
        foreach (var e in input.Envelopes)
            budget.Plan(e.CategoryId, e.PlannedAmount);

        foreach (var i in input.Incomes ?? [])
            budget.PlanIncome(i.CategoryId, i.PlannedAmount);

        await uow.SaveChangesAsync(ct);
        return budget.Id;
    }
}
