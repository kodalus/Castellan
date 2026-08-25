using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record TransferProposalOverview(
    Guid GroupId,
    string FromAccountName,
    string ToAccountName,
    Money Amount,
    DateTimeOffset OccurredAt,
    bool ToIsSavings);

public sealed class GetTransferProposalsUseCase(
    ITransactionRepository transactions,
    IAccountRepository accounts)
{
    public async Task<IReadOnlyList<TransferProposalOverview>> ExecuteAsync(CancellationToken ct = default)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var allAccounts = await accounts.ListAsync(ct);
        var accountMap = allAccounts.ToDictionary(a => a.Id, a => a);

        var result = new List<TransferProposalOverview>();

        foreach (var group in proposed.GroupBy(t => t.ProposedTransferGroupId!.Value))
        {
            var pair = group.ToList();
            if (pair.Count != 2) continue;

            // Outgoing = negative amount
            var from = pair[0].Amount.IsNegative ? pair[0] : pair[1];
            var to   = pair[0].Amount.IsNegative ? pair[1] : pair[0];

            var fromAccount = accountMap.GetValueOrDefault(from.AccountId);
            var toAccount   = accountMap.GetValueOrDefault(to.AccountId);

            result.Add(new TransferProposalOverview(
                group.Key,
                fromAccount?.Name ?? "?",
                toAccount?.Name ?? "?",
                new Money(Math.Abs(from.Amount.Grosze)),
                from.OccurredAt,
                // Przelew NA własne oszczędnościowe to zwykle odkładanie na rezerwę,
                // a nie zwykłe przekładanie pieniędzy — ekran ma o to dopytać.
                toAccount?.Kind == AccountKind.Savings));
        }

        return result;
    }
}
