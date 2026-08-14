using Castellan.Application.Repositories;

namespace Castellan.Application.UseCases;

public sealed class RejectTransferUseCase(
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(Guid proposedGroupId, CancellationToken ct = default)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var pair = proposed.Where(t => t.ProposedTransferGroupId == proposedGroupId).ToList();
        foreach (var tx in pair)
            tx.ClearTransferProposal();

        await uow.SaveChangesAsync(ct);
    }
}
