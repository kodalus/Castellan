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

        await transactions.RemoveAsync(tx, ct);
        await uow.SaveChangesAsync(ct);
    }
}
