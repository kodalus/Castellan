using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

public sealed class DeleteTransactionUseCase(
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(TransactionId id, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Transaction {id} not found.");

        // Przelew to para wpisów na dwóch kontach. Skasowanie jednej strony
        // zostawiłoby drugą jako sierotę: nadal wyłączoną z kopert, ale bez pary,
        // przez co saldo jednego konta zmieniłoby się bez odpowiednika na drugim.
        if (tx.TransferGroupId is { } groupId)
        {
            foreach (var leg in await transactions.ListByTransferGroupAsync(groupId, ct))
                await transactions.RemoveAsync(leg, ct);
        }
        else
        {
            await transactions.RemoveAsync(tx, ct);
        }

        await uow.SaveChangesAsync(ct);
    }
}
