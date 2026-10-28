using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

public sealed class DeleteTransactionUseCase(
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    /// <summary>
    /// Zwraca identyfikatory wpisów, które naprawdę zniknęły — dla przelewu są to DWA
    /// wpisy, nie jeden. Bez tego ekran usuwał ze swojej listy tylko dotknięty wiersz,
    /// druga noga zostawała widoczna, a tapnięcie w nią kończyło się wyjątkiem
    /// „Transaction … not found" o czymś, czego już nie było.
    ///
    /// Brak transakcji nie jest błędem: dwa szybkie przesunięcia albo nieodświeżona
    /// lista to zwykłe sytuacje, a nie awaria. Tak samo zachowuje się usuwanie aktywa.
    /// </summary>
    /// <param name="removeProposedPair">
    /// Dotyczy przelewu WYKRYTEGO, ale jeszcze niepotwierdzonego. Para z propozycji może
    /// być prawdziwym przelewem (wtedy obie strony mają zniknąć razem) albo przypadkowym
    /// zestawieniem dwóch niezwiązanych kwot (wtedy druga strona jest prawdziwą
    /// transakcją i nie wolno jej ruszać). Tego nie da się rozstrzygnąć w kodzie —
    /// decyduje użytkownik.
    /// </param>
    public async Task<IReadOnlyList<TransactionId>> ExecuteAsync(
        TransactionId id, bool removeProposedPair = false, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(id, ct);
        if (tx is null) return [];

        // Przelew to para wpisów na dwóch kontach. Skasowanie jednej strony
        // zostawiłoby drugą jako sierotę: nadal wyłączoną z kopert, ale bez pary,
        // przez co saldo jednego konta zmieniłoby się bez odpowiednika na drugim.
        var removed = new List<TransactionId>();
        if (tx.TransferGroupId is { } groupId)
        {
            foreach (var leg in await transactions.ListByTransferGroupAsync(groupId, ct))
            {
                await transactions.RemoveAsync(leg, ct);
                removed.Add(leg.Id);
            }
        }
        // Przelew wykryty, ale jeszcze niepotwierdzony, trzyma pare pod INNYM polem —
        // ProposedTransferGroupId zamiast TransferGroupId. Tego przypadku tu nie bylo,
        // wiec usuwalo sie tylko dotknieta noge, a druga zostawala na swoim koncie:
        // saldo nie wracalo do stanu sprzed przelewu, mimo ze przelew "zniknal".
        else if (tx.ProposedTransferGroupId is { } proposedId)
        {
            var legs = (await transactions.ListProposedTransfersAsync(ct))
                .Where(t => t.ProposedTransferGroupId == proposedId)
                .ToList();

            foreach (var leg in legs)
            {
                if (removeProposedPair || leg.Id == tx.Id)
                {
                    await transactions.RemoveAsync(leg, ct);
                    removed.Add(leg.Id);
                }
                else
                {
                    // Noga, ktora zostaje, nie moze zostac z propozycja wskazujaca na
                    // grupe bez pary — takiej propozycji nie da sie juz potwierdzic
                    // i wpis utknalby z napisem "potwierdz w Skrzynce" na zawsze.
                    leg.ClearTransferProposal();
                }
            }
        }
        else
        {
            await transactions.RemoveAsync(tx, ct);
            removed.Add(tx.Id);
        }

        await uow.SaveChangesAsync(ct);
        return removed;
    }
}
