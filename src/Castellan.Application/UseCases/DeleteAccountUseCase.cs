using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Usuwa konto razem z jego historią.
///
/// To działanie jest NIEODWRACALNE i kosztuje więcej, niż sugeruje nazwa: transakcje
/// mają klucz obcy do konta z kasowaniem kaskadowym, więc znikają razem z nim — a z nimi
/// faktyczne kwoty w kopertach tych miesięcy, w statystykach i w średniej pod poduszką.
/// Dlatego ekran ma najpierw policzyć, co zniknie, i zaproponować archiwizację.
///
/// Przelewy idą parami, tak samo jak przy usuwaniu pojedynczej transakcji: skasowanie
/// jednej nogi zostawiłoby drugą jako sierotę — nadal wyłączoną z kopert, ale bez pary,
/// więc saldo drugiego konta zmieniłoby się bez odpowiednika. Partnerskie nogi leżą na
/// INNYCH kontach, więc usunięcie konta rusza też dane, których użytkownik nie tykał.
/// Trzeba mu to powiedzieć liczbą, zanim potwierdzi.
/// </summary>
public sealed class DeleteAccountUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    /// <param name="OwnTransactions">Wpisy leżące na tym koncie.</param>
    /// <param name="PartnerTransactions">Drugie nogi przelewów, leżące na innych kontach.</param>
    public sealed record Impact(int OwnTransactions, int PartnerTransactions)
    {
        public int Total => OwnTransactions + PartnerTransactions;
        public bool IsEmpty => Total == 0;
    }

    /// <summary>Co zniknie, bez zmieniania czegokolwiek — do treści potwierdzenia.</summary>
    public async Task<Impact> PreviewAsync(AccountId id, CancellationToken ct = default)
    {
        var (own, partners) = await GatherAsync(id, ct);
        return new Impact(own.Count, partners.Count);
    }

    public async Task<Impact> ExecuteAsync(AccountId id, CancellationToken ct = default)
    {
        var account = await accounts.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Account {id} not found.");

        var (own, partners) = await GatherAsync(id, ct);

        // Partnerskie nogi trzeba skasować JAWNIE — kaskada ich nie ruszy, bo wiszą
        // przy innych kontach. Własne wpisy zniknęłyby kaskadą, ale kasujemy je tu
        // tak samo jawnie: zachowanie nie ma zależeć od ustawienia klucza obcego,
        // które widać tylko w konfiguracji EF.
        foreach (var tx in partners.Concat(own))
            await transactions.RemoveAsync(tx, ct);

        await accounts.RemoveAsync(account, ct);
        await uow.SaveChangesAsync(ct);

        return new Impact(own.Count, partners.Count);
    }

    private async Task<(List<Domain.Aggregates.Transaction> own, List<Domain.Aggregates.Transaction> partners)>
        GatherAsync(AccountId id, CancellationToken ct)
    {
        var own = (await transactions.ListForAccountAsync(id, ct)).ToList();

        var partners = new List<Domain.Aggregates.Transaction>();
        var seen = new HashSet<Guid>();

        foreach (var groupId in own.Select(t => t.TransferGroupId).OfType<Guid>())
        {
            if (!seen.Add(groupId)) continue;

            partners.AddRange((await transactions.ListByTransferGroupAsync(groupId, ct))
                .Where(leg => leg.AccountId != id));
        }

        return (own, partners);
    }
}
