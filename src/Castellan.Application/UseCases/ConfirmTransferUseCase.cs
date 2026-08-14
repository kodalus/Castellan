using Castellan.Application.Repositories;

namespace Castellan.Application.UseCases;

public sealed class ConfirmTransferUseCase(
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(Guid proposedGroupId, CancellationToken ct = default)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var pair = proposed.Where(t => t.ProposedTransferGroupId == proposedGroupId).ToList();
        if (pair.Count != 2) return;

        var groupId = Guid.NewGuid();
        pair[0].SetTransferGroup(groupId);
        pair[1].SetTransferGroup(groupId);

        await uow.SaveChangesAsync(ct);
    }
}
