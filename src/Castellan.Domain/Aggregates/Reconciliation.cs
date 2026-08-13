using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Reconciliation
{
    public ReconciliationId Id { get; private set; }
    public AccountId AccountId { get; private set; }
    public Money ObservedBalance { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public Money PreviousBalance { get; private set; }
    public DateTimeOffset PreviousAt { get; private set; }
    public Money RecordedDelta { get; private set; }
    public Money Discrepancy { get; private set; }
    public TransactionId? GeneratedTransactionId { get; private set; }

    private Reconciliation() { }

    // N-5: Discrepancy > 0 (potential unrecorded income) is returned to caller — no auto-create
    // N-6: does not touch past transactions; only adds new ones via GeneratedTransactionId
    public static Reconciliation Create(
        AccountId accountId,
        Money observedBalance,
        DateTimeOffset observedAt,
        Money previousBalance,
        DateTimeOffset previousAt,
        Money recordedDelta)
    {
        return new Reconciliation
        {
            Id = ReconciliationId.New(),
            AccountId = accountId,
            ObservedBalance = observedBalance,
            ObservedAt = observedAt,
            PreviousBalance = previousBalance,
            PreviousAt = previousAt,
            RecordedDelta = recordedDelta,
            Discrepancy = (observedBalance - previousBalance) - recordedDelta,
        };
    }

    public void LinkGeneratedTransaction(TransactionId id) =>
        GeneratedTransactionId = id;
}
