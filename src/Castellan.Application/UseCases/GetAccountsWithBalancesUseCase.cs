using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record AccountWithBalance(
    AccountId Id,
    string Name,
    AccountKind Kind,
    Money CurrentBalance,
    Money LastReconciledBalance,
    DateTimeOffset LastReconciledAt,
    bool IsArchived);

public sealed class GetAccountsWithBalancesUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions)
{
    public async Task<IReadOnlyList<AccountWithBalance>> ExecuteAsync(CancellationToken ct = default)
    {
        var accountList = await accounts.ListAsync(ct);
        var result = new List<AccountWithBalance>(accountList.Count);

        foreach (var account in accountList)
        {
            var txs = await transactions.ListForAccountAsync(account.Id, ct);
            var postReconciliation = txs
                .Where(t => t.OccurredAt > account.LastReconciledAt && !t.SupersededById.HasValue)
                .Select(t => t.Amount)
                .Sum();

            result.Add(new AccountWithBalance(
                account.Id,
                account.Name,
                account.Kind,
                account.LastReconciledBalance + postReconciliation,
                account.LastReconciledBalance,
                account.LastReconciledAt,
                account.IsArchived));
        }

        return result;
    }
}
