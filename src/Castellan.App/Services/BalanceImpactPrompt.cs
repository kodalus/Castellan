using Castellan.Application.UseCases;
using Castellan.Domain;

namespace Castellan.App.Services;

/// <summary>
/// Uprzedza, kiedy zapis nie ruszy salda konta, bo ma datę sprzed ostatniego uzgodnienia.
/// Powód i granicę opisuje <see cref="CheckBalanceImpactUseCase"/>; tutaj zostaje samo
/// pytanie — wspólne dla wszystkich ekranów, które zapisują transakcję z wybraną datą,
/// żeby treść ostrzeżenia nie rozjechała się między nimi.
///
/// Pytanie, a nie blokada: data sprzed uzgodnienia bywa poprawna (dopisany zapomniany
/// zakup), a wtedy brak zmiany salda jest właściwy. Złe jest tylko to, żeby działo się
/// po cichu.
/// </summary>
public sealed class BalanceImpactPrompt(CheckBalanceImpactUseCase check)
{
    /// <summary>
    /// Zwraca true, gdy można zapisywać: albo saldo się zmieni, albo użytkownik
    /// potwierdził, że i tak chce zapisać.
    /// </summary>
    public async Task<bool> ConfirmAsync(
        IReadOnlyList<(AccountId Account, DateTimeOffset OccurredAt)> entries,
        CancellationToken ct = default)
    {
        var ignored = await check.ExecuteAsync(entries, ct);
        if (ignored.Count == 0) return true;

        // Bez okna nie ma jak zapytać — wtedy lepiej zapisać niż po cichu porzucić wpis.
        if (Shell.Current?.CurrentPage is not Page page) return true;

        var lines = string.Join("\n", ignored.Select(i =>
            $"• „{i.AccountName}” — uzgodnione {i.ReconciledAt.ToLocalTime():dd.MM.yyyy, HH:mm}"));

        var isTransfer = entries.Count > 1;

        var (title, explanation) = (isTransfer, ignored.Count) switch
        {
            (true, 1) => ("Salda kont się rozjadą",
                "Przelew to jedno zdarzenie na dwóch kontach. Ta strona nie ruszy salda "
                + "swojego konta, bo uzgodnienie przyjęło, że ta kwota już w nim jest — "
                + "a druga strona swoje saldo zmieni. Razem przestaną się zgadzać."),

            (true, _) => ("Salda kont się nie zmienią",
                "Obie strony przelewu mają datę sprzed uzgodnienia swoich kont, więc żadne "
                + "saldo nie drgnie. Przelew zapisze się na liście transakcji."),

            _ => ("Saldo konta się nie zmieni",
                "Uzgodnienie przyjęło, że ta kwota jest już w saldzie konta, więc zapis "
                + "z wcześniejszą datą go nie ruszy. Transakcja i tak trafi do kopert "
                + "i na listę transakcji."),
        };

        return await page.DisplayAlertAsync(title, $"{lines}\n\n{explanation}", "Zapisz mimo to", "Anuluj");
    }
}
