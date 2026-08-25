using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class ConfirmTransferUseCase(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IFundRepository funds,
    IUnitOfWork uow)
{
    public const string ReserveCategoryName = "Rezerwy";

    /// <summary>Zwykły przelew między własnymi kontami — obie nogi wypadają z budżetu.</summary>
    public async Task ExecuteAsync(Guid proposedGroupId, CancellationToken ct = default)
    {
        var pair = await FindPairAsync(proposedGroupId, ct);
        if (pair is null) return;

        var groupId = Guid.NewGuid();
        pair.Value.From.SetTransferGroup(groupId);
        pair.Value.To.SetTransferGroup(groupId);

        await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Odkładanie na rezerwę: noga wychodząca zostaje wydatkiem w kategorii „Rezerwy",
    /// noga przychodząca wypada z budżetu. Gdy podany jest fundusz, jego saldo rośnie
    /// o tę samą kwotę — dzięki temu jedna czynność w banku zamyka temat w aplikacji.
    /// </summary>
    public async Task ExecuteAsReserveAsync(Guid proposedGroupId, FundId? contributeTo, CancellationToken ct = default)
    {
        var pair = await FindPairAsync(proposedGroupId, ct);
        if (pair is null) return;

        var reserve = (await categories.ListAsync(ct))
            .FirstOrDefault(c => c.Name.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase));

        // Bez kategorii „Rezerwy" nie ma czego obciążyć — wtedy zwykły przelew jest
        // bezpieczniejszy niż wydatek wrzucony do przypadkowej kategorii.
        if (reserve is null)
        {
            await ExecuteAsync(proposedGroupId, ct);
            return;
        }

        var (from, to) = pair.Value;
        from.MarkAsReserveMove(reserve.Id);
        to.SetTransferGroup(Guid.NewGuid());

        if (contributeTo is { } fundId)
        {
            var fund = await funds.GetAsync(fundId, ct);
            fund?.Contribute(new Money(Math.Abs(from.Amount.Grosze)));
        }

        await uow.SaveChangesAsync(ct);
    }

    private async Task<(Domain.Aggregates.Transaction From, Domain.Aggregates.Transaction To)?>
        FindPairAsync(Guid proposedGroupId, CancellationToken ct)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var pair = proposed.Where(t => t.ProposedTransferGroupId == proposedGroupId).ToList();
        if (pair.Count != 2) return null;

        var from = pair[0].Amount.IsNegative ? pair[0] : pair[1];
        var to = pair[0].Amount.IsNegative ? pair[1] : pair[0];
        return (from, to);
    }
}
