namespace Castellan.Domain;

public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct CategoryId(Guid Value)
{
    public static CategoryId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct TransactionId(Guid Value)
{
    public static TransactionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct MonthBudgetId(Guid Value)
{
    public static MonthBudgetId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct ReconciliationId(Guid Value)
{
    public static ReconciliationId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct RawNotificationId(Guid Value)
{
    public static RawNotificationId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct CategoryRuleId(Guid Value)
{
    public static CategoryRuleId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
