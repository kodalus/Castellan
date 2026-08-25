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
    public async Task<IReadOnlyList<TransactionId>> ExecuteAsync(TransactionId id, CancellationToken ct = default)
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
        else
        {
            await transactions.RemoveAsync(tx, ct);
            removed.Add(tx.Id);
        }

        await uow.SaveChangesAsync(ct);
        return removed;
    }
}
