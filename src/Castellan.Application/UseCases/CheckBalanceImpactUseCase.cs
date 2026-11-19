using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Saldo konta liczy się od ostatniego uzgodnienia: zaobserwowana kwota plus wszystko,
/// co wydarzyło się PÓŹNIEJ. Reguła jest słuszna — uzgodnienie mówi „tyle naprawdę było
/// na koncie tego dnia", więc wcześniejsze transakcje są już w tej kwocie zawarte
/// i doliczenie ich drugi raz zepsułoby saldo.
///
/// Wada nie jest w regule, tylko w jej MILCZENIU. Transakcja zapisana z datą sprzed
/// uzgodnienia zapisuje się poprawnie, widać ją na liście i w kopertach, ale saldo konta
/// nie drgnie — i nic tego nie mówi. Przy przelewie jest gorzej: to jedno zdarzenie na
/// dwóch kontach, z których każde ma własne uzgodnienie. Gdy data wypadnie po uzgodnieniu
/// jednego konta, a przed uzgodnieniem drugiego, jedna noga rusza saldo, a druga nie —
/// i w sumie kont pieniądze POWSTAJĄ albo ZNIKAJĄ.
///
/// Ten use case odpowiada na pytanie „czy ten zapis w ogóle ruszy saldo", żeby ekran
/// mógł uprzedzić, zamiast zostawić użytkownika z liczbą, która nie chce się zmienić.
/// </summary>
public sealed class CheckBalanceImpactUseCase(IAccountRepository accounts)
{
    /// <summary>Konto, którego saldo pozostanie bez zmian, i data uzgodnienia, która o tym decyduje.</summary>
    public sealed record Ignored(string AccountName, DateTimeOffset ReconciledAt);

    public async Task<IReadOnlyList<Ignored>> ExecuteAsync(
        IReadOnlyList<(AccountId Account, DateTimeOffset OccurredAt)> entries,
        CancellationToken ct = default)
    {
        var result = new List<Ignored>();

        foreach (var (accountId, occurredAt) in entries)
        {
            var account = await accounts.GetAsync(accountId, ct);
            if (account is null) continue;

            // Ostro „nie później" — dokładnie ta sama granica, której używa liczenie salda
            // (t.OccurredAt > LastReconciledAt). Równość znaczy „nie liczy się".
            if (occurredAt <= account.LastReconciledAt)
                result.Add(new Ignored(account.Name, account.LastReconciledAt));
        }

        return result;
    }
}
