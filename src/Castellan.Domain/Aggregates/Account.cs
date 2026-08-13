using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Account
{
    public AccountId Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? BankKey { get; private set; }
    public AccountKind Kind { get; private set; }
    public LiquidityTier LiquidityTier { get; private set; }
    public Money LastReconciledBalance { get; private set; }
    public DateTimeOffset LastReconciledAt { get; private set; }
    public bool IsArchived { get; private set; }

    private Account() { }

    public static Account Create(
        string name,
        AccountKind kind,
        Money initialBalance,
        DateTimeOffset reconciledAt,
        string? bankKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Account
        {
            Id = AccountId.New(),
            Name = name.Trim(),
            Kind = kind,
            BankKey = bankKey,
            LiquidityTier = LiquidityTier.Immediate,
            LastReconciledBalance = initialBalance,
            LastReconciledAt = reconciledAt,
            IsArchived = false,
        };
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Archive() => IsArchived = true;

    public void Reconcile(Money balance, DateTimeOffset at)
    {
        LastReconciledBalance = balance;
        LastReconciledAt = at;
    }
}
