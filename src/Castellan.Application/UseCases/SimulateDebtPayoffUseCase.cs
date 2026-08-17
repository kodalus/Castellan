using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record DebtPayoffStep(
    DebtId Id,
    string Name,
    Money Balance,
    int MonthCleared,
    DateOnly DateCleared);

public sealed record DebtPayoffPlan(
    Money TotalDebt,
    Money MinimumMonthly,
    Money SimulatedMonthly,
    int? MonthsToFreedom,
    DateOnly? FreedomDate,
    IReadOnlyList<DebtPayoffStep> Steps,
    bool BelowMinimum,
    bool ExceedsHorizon);

/// <summary>
/// Symuluje wychodzenie z długów metodą kuli śnieżnej: co miesiąc każdy dług dostaje
/// swoją minimalną ratę, a cała nadwyżka idzie w najmniejsze saldo. Gdy jeden dług
/// znika, jego rata dołącza do nadwyżki i kolejny spłaca się szybciej — to właśnie
/// ten efekt kaskady sprawia, że wynik jest wyraźnie lepszy niż suma pojedynczych
/// harmonogramów.
///
/// UWAGA: symulacja nie zna odsetek (moduł ich nie śledzi), więc dla oprocentowanych
/// zobowiązań wynik jest optymistyczny. Ekran musi to powiedzieć wprost.
/// </summary>
public sealed class SimulateDebtPayoffUseCase(IDebtRepository debts)
{
    // Zabezpieczenie przed nieskończoną pętlą, gdy wpłata ledwo pokrywa minimum.
    private const int HorizonMonths = 600;

    public async Task<DebtPayoffPlan> ExecuteAsync(Money? monthlyBudget = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var active = (await debts.ListAsync(ct))
            .Where(d => !d.IsArchived && !d.IsPaidOff)
            .OrderBy(d => d.Balance.Grosze)
            .ToList();

        var totalDebt = new Money(active.Sum(d => d.Balance.Grosze));
        var minimumMonthly = new Money(active.Sum(d => d.InstallmentAmount.Grosze));
        var budget = monthlyBudget?.Grosze is > 0 ? monthlyBudget!.Value : minimumMonthly;

        if (active.Count == 0)
        {
            return new DebtPayoffPlan(
                Money.Zero, Money.Zero, budget, 0, today, [], false, false);
        }

        // Bez żadnej wpłaty dług nigdy nie zniknie — lepiej to powiedzieć niż liczyć.
        if (budget.Grosze <= 0)
        {
            return new DebtPayoffPlan(
                totalDebt, minimumMonthly, budget, null, null, [], true, false);
        }

        var balances = active.Select(d => d.Balance.Grosze).ToArray();
        var minimums = active.Select(d => d.InstallmentAmount.Grosze).ToArray();
        var steps = new List<DebtPayoffStep>();

        var month = 0;
        while (balances.Any(b => b > 0) && month < HorizonMonths)
        {
            month++;
            var remaining = budget.Grosze;

            // 1) Minimalne raty — obsługa zobowiązań, żeby nie rosły zaległości.
            for (var i = 0; i < balances.Length && remaining > 0; i++)
            {
                if (balances[i] <= 0) continue;
                var pay = Math.Min(Math.Min(minimums[i], balances[i]), remaining);
                balances[i] -= pay;
                remaining -= pay;
            }

            // 2) Nadwyżka w całości na najmniejsze pozostałe saldo — sedno kuli śnieżnej.
            for (var i = 0; i < balances.Length && remaining > 0; i++)
            {
                if (balances[i] <= 0) continue;
                var pay = Math.Min(balances[i], remaining);
                balances[i] -= pay;
                remaining -= pay;
            }

            for (var i = 0; i < balances.Length; i++)
            {
                if (balances[i] > 0 || steps.Any(s => s.Id == active[i].Id)) continue;
                steps.Add(new DebtPayoffStep(
                    active[i].Id,
                    active[i].Name,
                    active[i].Balance,
                    month,
                    new DateOnly(today.Year, today.Month, 1).AddMonths(month)));
            }
        }

        var finished = balances.All(b => b <= 0);

        return new DebtPayoffPlan(
            totalDebt,
            minimumMonthly,
            budget,
            finished ? month : null,
            finished ? new DateOnly(today.Year, today.Month, 1).AddMonths(month) : null,
            steps,
            BelowMinimum: budget.Grosze < minimumMonthly.Grosze,
            ExceedsHorizon: !finished);
    }
}
