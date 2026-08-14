using Castellan.Application.Repositories;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record TransferProposalOverview(
    Guid GroupId,
    string FromAccountName,
    string ToAccountName,
    Money Amount,
    DateTimeOffset OccurredAt);

public sealed class GetTransferProposalsUseCase(
    ITransactionRepository transactions,
    IAccountRepository accounts)
{
    public async Task<IReadOnlyList<TransferProposalOverview>> ExecuteAsync(CancellationToken ct = default)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var allAccounts = await accounts.ListAsync(ct);
        var accountMap = allAccounts.ToDictionary(a => a.Id, a => a.Name);

        var result = new List<TransferProposalOverview>();

        foreach (var group in proposed.GroupBy(t => t.ProposedTransferGroupId!.Value))
        {
            var pair = group.ToList();
            if (pair.Count != 2) continue;

            // Outgoing = negative amount
            var from = pair[0].Amount.IsNegative ? pair[0] : pair[1];
            var to   = pair[0].Amount.IsNegative ? pair[1] : pair[0];

            var fromName = accountMap.TryGetValue(from.AccountId, out var n0) ? n0 : "?";
            var toName   = accountMap.TryGetValue(to.AccountId,   out var n1) ? n1 : "?";

            result.Add(new TransferProposalOverview(
                group.Key,
                fromName,
                toName,
                new Money(Math.Abs(from.Amount.Grosze)),
                from.OccurredAt));
        }

        return result;
    }
}
