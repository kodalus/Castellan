using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Transaction
{
    public TransactionId Id { get; private set; }
    public AccountId AccountId { get; private set; }
    public Money Amount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public CategoryId CategoryId { get; private set; }
    public string? RawMerchant { get; private set; }
    public string? MerchantKey { get; private set; }
    public string? Note { get; private set; }
    public TransactionSource Source { get; private set; }
    public TransactionKind Kind { get; private set; }
    public Guid? TransferGroupId { get; private set; }
    public TransactionId? SupersededById { get; private set; }
    public Guid? RawNotificationId { get; private set; }

    // N-4: superseded or transfer excluded from calculations
    public bool IsExcludedFromCalculations =>
        Kind == TransactionKind.Transfer || SupersededById.HasValue;

    private Transaction() { }

    // N-2: CategoryId always set — defaults to Unsorted when called from notification path
    public static Transaction CreateManual(
        AccountId accountId,
        Money amount,
        DateTimeOffset occurredAt,
        CategoryId categoryId,
        string? note = null)
    {
        return new Transaction
        {
            Id = TransactionId.New(),
            AccountId = accountId,
            Amount = amount,
            OccurredAt = occurredAt,
            CategoryId = categoryId,
            Note = note,
            Source = TransactionSource.Manual,
            Kind = TransactionKind.Regular,
        };
    }

    public void AssignCategory(CategoryId categoryId) => CategoryId = categoryId;

    public void SetNote(string? note) => Note = note;

    public void Supersede(TransactionId byId) => SupersededById = byId;

    public void SetTransferGroup(Guid groupId)
    {
        TransferGroupId = groupId;
        Kind = TransactionKind.Transfer;
        CategoryId = Category.TransferId;
    }
}
