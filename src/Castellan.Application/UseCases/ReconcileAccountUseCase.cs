using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class ReconcileAccountUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IReconciliationRepository reconciliations,
    IUnitOfWork uow)
{
    public sealed record Input(
        AccountId AccountId,
        Money ObservedBalance,
        DateTimeOffset ObservedAt);

    public sealed record Output(
        Money Discrepancy,
        // true when discrepancy > 0: unrecorded income or duplicate expense — user must decide
        bool RequiresDecision,
        TransactionId? GeneratedTransactionId);

    public async Task<Output> ExecuteAsync(Input input, CancellationToken ct = default)
    {
        var account = await accounts.GetAsync(input.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {input.AccountId} not found.");

        // Collect transactions in the reconciliation window (N-6: never modify past)
        var allTxs = await transactions.ListForAccountAsync(input.AccountId, ct);
        var windowTxs = allTxs
            .Where(t => t.OccurredAt > account.LastReconciledAt
                     && t.OccurredAt <= input.ObservedAt
                     && !t.SupersededById.HasValue)
            .ToList();

        var recordedDelta = windowTxs.Select(t => t.Amount).Sum();

        var rec = Reconciliation.Create(
            input.AccountId,
            input.ObservedBalance,
            input.ObservedAt,
            account.LastReconciledBalance,
            account.LastReconciledAt,
            recordedDelta);

        TransactionId? generatedId = null;

        if (rec.Discrepancy < Money.Zero)
        {
            // Unrecorded expenses → auto-create Unidentified transaction (N-5, N-6)
            var tx = Transaction.CreateReconciliation(input.AccountId, rec.Discrepancy, input.ObservedAt);
            await transactions.AddAsync(tx, ct);
            rec.LinkGeneratedTransaction(tx.Id);
            generatedId = tx.Id;
        }

        await reconciliations.AddAsync(rec, ct);

        // Always advance the reconciliation point (user sees discrepancy > 0 in the UI)
        account.Reconcile(input.ObservedBalance, input.ObservedAt);

        await uow.SaveChangesAsync(ct);

        return new Output(rec.Discrepancy, rec.Discrepancy > Money.Zero, generatedId);
    }
}
