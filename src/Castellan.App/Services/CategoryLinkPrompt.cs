using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;

namespace Castellan.App.Services;

/// <summary>
/// Niektóre kategorie wydatków są tylko „przystankiem” — pieniądze nie znikają, lecz
/// trafiają do konkretnego funduszu albo zmniejszają konkretny dług. Po zapisaniu
/// takiego wydatku pytamy, którego dotyczy, i od razu aktualizujemy saldo, żeby nie
/// trzeba było robić drugiej czynności na innym ekranie (i o niej zapominać).
///
/// Wspólne miejsce dla wszystkich ścieżek dodawania wydatku — pełnego formularza,
/// szybkiego dodawania i kategoryzowania ze skrzynki — żeby zachowanie nie rozjechało
/// się między nimi.
/// </summary>
public sealed class CategoryLinkPrompt(
    IFundRepository funds,
    IDebtRepository debts,
    ContributeToFundUseCase contributeToFund,
    ApplyDebtPaymentUseCase applyDebtPayment)
{
    public const string ReserveCategoryName = "Rezerwy";
    public const string DebtCategoryName = "Kredyty i pożyczki";

    /// <summary>
    /// Uruchamia pytanie, jeśli kategoria jest powiązana z funduszami lub długami.
    /// Kwota musi być dodatnia (wartość bezwzględna wydatku).
    /// </summary>
    public async Task OfferAsync(string? categoryName, Money amount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(categoryName) || amount.Grosze <= 0) return;

        if (categoryName.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase))
            await OfferFundAsync(amount, ct);
        else if (categoryName.Equals(DebtCategoryName, StringComparison.OrdinalIgnoreCase))
            await OfferDebtAsync(amount, ct);
    }

    private async Task OfferFundAsync(Money amount, CancellationToken ct)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        var active = (await funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (active.Count == 0) return;

        var choice = await page.DisplayActionSheet(
            "Do którego funduszu wpłacić?", "Pomiń", null, [.. active.Select(f => f.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Pomiń") return;

        var fund = active.FirstOrDefault(f => f.Name == choice);
        if (fund is null) return;

        await contributeToFund.ExecuteAsync(fund.Id, amount, ct);
    }

    private async Task OfferDebtAsync(Money amount, CancellationToken ct)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        // Spłacone zobowiązania pomijamy — nie ma czego zmniejszać, a zaśmiecałyby listę.
        var active = (await debts.ListAsync(ct))
            .Where(d => !d.IsArchived && !d.IsPaidOff)
            .OrderBy(d => d.Balance.Grosze)
            .ToList();
        if (active.Count == 0) return;

        var choice = await page.DisplayActionSheet(
            "Na który kredyt poszła rata?", "Pomiń", null, [.. active.Select(d => d.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Pomiń") return;

        var debt = active.FirstOrDefault(d => d.Name == choice);
        if (debt is null) return;

        await applyDebtPayment.ExecuteAsync(debt.Id, amount, ct);
    }
}
