using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Account
{
    public AccountId Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? BankKey { get; private set; }
    public AccountKind Kind { get; private set; }
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

    /// <summary>
    /// Typ konta wolno poprawić: rozstrzyga o tym, czy przelew NA nie jest pytaniem
    /// „odkładasz czy przekładasz", więc pomyłka przy zakładaniu ma widoczne skutki.
    /// </summary>
    public void SetKind(AccountKind kind) => Kind = kind;

    /// <summary>
    /// Bank konta — do dopasowywania powiadomień. Puste znaczy „nie wiem": wtedy
    /// dopasowanie zgaduje po nazwie, tak jak dotąd. Jawny wybór bije zgadywanie,
    /// bo nazwę konta użytkownik nadaje dla siebie, a nie dla parsera.
    /// </summary>
    public void SetBank(string? bankKey) =>
        BankKey = string.IsNullOrWhiteSpace(bankKey) ? null : bankKey.Trim();

    public void Archive() => IsArchived = true;

    public void Reconcile(Money balance, DateTimeOffset at)
    {
        LastReconciledBalance = balance;
        LastReconciledAt = at;
    }
}
