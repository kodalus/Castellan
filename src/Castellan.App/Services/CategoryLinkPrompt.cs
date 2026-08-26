using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
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
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ContributeToFundUseCase contributeToFund,
    ApplyDebtPaymentUseCase applyDebtPayment,
    RecordReserveTransferUseCase recordReserveTransfer)
{
    public const string ReserveCategoryName = "Rezerwy";
    public const string DebtCategoryName = "Kredyty i pożyczki";

    /// <summary>
    /// Uruchamia pytanie, jeśli kategoria jest powiązana z funduszami lub długami.
    /// Kwota musi być dodatnia (wartość bezwzględna wydatku).
    /// </summary>
    public async Task OfferAsync(
        string? categoryName,
        Money amount,
        TransactionId? transactionId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(categoryName) || amount.Grosze <= 0) return;

        if (categoryName.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase))
            await OfferReserveAsync(amount, transactionId, ct);
        else if (categoryName.Equals(DebtCategoryName, StringComparison.OrdinalIgnoreCase))
            await OfferDebtAsync(amount, ct);
    }

    /// <summary>
    /// Dwa pytania, oba do pominięcia: na co odkładasz i gdzie te pieniądze wylądowały.
    ///
    /// Drugie jest nowsze i ważniejsze. Sam wydatek w „Rezerwach" mówi tylko, ile ubyło
    /// z konta źródłowego — bez wskazania konta docelowego saldo jednego maleje, drugiego
    /// nie rośnie, a pieniądze ZNIKAJĄ z Majątku, mimo że użytkownik nadal je ma.
    /// </summary>
    private async Task OfferReserveAsync(Money amount, TransactionId? transactionId, CancellationToken ct)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        await OfferFundAsync(page, amount, ct);
        await OfferDestinationAsync(page, transactionId, ct);
    }

    private async Task OfferFundAsync(Page page, Money amount, CancellationToken ct)
    {
        var active = (await funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (active.Count == 0) return;

        var choice = await page.DisplayActionSheetAsync(
            "Do którego funduszu wpłacić?", "Pomiń", null, [.. active.Select(f => f.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Pomiń") return;

        var fund = active.FirstOrDefault(f => f.Name == choice);
        if (fund is null) return;

        // Sama zmiana salda funduszu: transakcja w kategorii „Rezerwy" już istnieje —
        // to ona wywołała to pytanie. Drugą stronę ruchu dopisuje pytanie o konto niżej.
        await contributeToFund.ExecuteAsync(fund.Id, amount, ct);
    }

    private async Task OfferDestinationAsync(Page page, TransactionId? transactionId, CancellationToken ct)
    {
        if (transactionId is not { } txId) return;

        var expense = await transactions.GetAsync(txId, ct);
        if (expense is null) return;

        // Konto źródłowe jest z listy wyłączone: przelew na samego siebie nic nie przenosi.
        var targets = (await accounts.ListAsync(ct))
            .Where(a => !a.IsArchived && a.Id != expense.AccountId)
            .ToList();
        if (targets.Count == 0) return;

        var choice = await page.DisplayActionSheetAsync(
            "Gdzie wylądowały te pieniądze?", "Zostają tam, gdzie były", null,
            [.. targets.Select(a => a.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Zostają tam, gdzie były") return;

        var destination = targets.FirstOrDefault(a => a.Name == choice);
        if (destination is null) return;

        await recordReserveTransfer.ExecuteAsync(txId, destination.Id, ct);
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

        var choice = await page.DisplayActionSheetAsync(
            "Na który kredyt poszła rata?", "Pomiń", null, [.. active.Select(d => d.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Pomiń") return;

        var debt = active.FirstOrDefault(d => d.Name == choice);
        if (debt is null) return;

        await applyDebtPayment.ExecuteAsync(debt.Id, amount, ct);
    }
}
