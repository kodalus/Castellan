using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record DebtSummary(
    DebtId Id,
    string Name,
    DebtKind Kind,
    string KindDisplay,
    Money Balance,
    Money InitialAmount,
    Money PaidOff,
    Money InstallmentAmount,
    int? InstallmentsRemaining,
    DateOnly? ProjectedPayoff,
    double Progress,
    bool IsPaidOff);

public sealed record DebtOverview(
    IReadOnlyList<DebtSummary> Items,
    Money TotalBalance,
    Money TotalMonthlyInstallments);

public sealed class GetDebtOverviewUseCase(IDebtRepository debts)
{
    public async Task<DebtOverview> ExecuteAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var all = await debts.ListAsync(ct);

        var items = all
            .Where(d => !d.IsArchived)
            .Select(d => new DebtSummary(
                d.Id,
                d.Name,
                d.Kind,
                KindDisplay(d.Kind),
                d.Balance,
                d.InitialAmount,
                d.PaidOff,
                d.InstallmentAmount,
                d.InstallmentsRemaining,
                d.ProjectedPayoff(today),
                d.Progress,
                d.IsPaidOff))
            // Spłacone na koniec, resztę od najmniejszego salda — to kolejność
            // "kuli śnieżnej": najszybciej domknięty dług zwalnia ratę na następny.
            .OrderBy(d => d.IsPaidOff ? 1 : 0)
            .ThenBy(d => d.Balance.Grosze)
            .ToList();

        return new DebtOverview(
            items,
            new Money(items.Sum(d => d.Balance.Grosze)),
            new Money(items.Where(d => !d.IsPaidOff).Sum(d => d.InstallmentAmount.Grosze)));
    }

    internal static string KindDisplay(DebtKind kind) => kind switch
    {
        DebtKind.Mortgage    => "Kredyt hipoteczny",
        DebtKind.CashLoan    => "Kredyt gotówkowy",
        DebtKind.Installment => "Zakup na raty",
        DebtKind.FromFamily  => "Pożyczka od bliskich",
        _                    => "Inne zobowiązanie",
    };
}
