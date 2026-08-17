using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

/// <summary>
/// Zobowiązanie — lustrzane odbicie funduszu: saldo maleje do zera zamiast rosnąć
/// do celu. Termin spłaty celowo nie jest przechowywany, tylko liczony z aktualnego
/// salda i raty: dzięki temu nadpłata od razu przesuwa datę wyjścia na zero, zamiast
/// zostawiać nieaktualną datę z umowy.
/// </summary>
public class Debt
{
    public DebtId Id { get; private set; }
    public string Name { get; private set; } = "";
    public DebtKind Kind { get; private set; }

    /// <summary>Kwota początkowa — potrzebna tylko do pokazania, ile już spłacono.</summary>
    public Money InitialAmount { get; private set; }

    public Money Balance { get; private set; }
    public Money InstallmentAmount { get; private set; }
    public bool IsArchived { get; private set; }

    private Debt() { }

    public static Debt Create(string name, DebtKind kind, Money initialAmount, Money installmentAmount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (initialAmount.Grosze < 0)
            throw new ArgumentException("Kwota długu nie może być ujemna.", nameof(initialAmount));
        if (installmentAmount.Grosze < 0)
            throw new ArgumentException("Rata nie może być ujemna.", nameof(installmentAmount));

        return new Debt
        {
            Id = DebtId.New(),
            Name = name.Trim(),
            Kind = kind,
            InitialAmount = initialAmount,
            Balance = initialAmount,
            InstallmentAmount = installmentAmount,
            IsArchived = false,
        };
    }

    /// <summary>Spłata obniża saldo, ale nigdy poniżej zera — nadpłata ponad dług to już nie dług.</summary>
    public void Pay(Money amount)
    {
        var magnitude = Math.Abs(amount.Grosze);
        Balance = new Money(Math.Max(0, Balance.Grosze - magnitude));
    }

    /// <summary>Korekta salda wprost z aplikacji banku — odsetki i prowizje potrafią je rozjechać.</summary>
    public void SetBalance(Money balance)
    {
        if (balance.Grosze < 0)
            throw new ArgumentException("Saldo długu nie może być ujemne.", nameof(balance));
        Balance = balance;
    }

    public void Update(string name, DebtKind kind, Money initialAmount, Money installmentAmount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Kind = kind;
        InitialAmount = initialAmount;
        InstallmentAmount = installmentAmount;
    }

    public void Archive() => IsArchived = true;

    public bool IsPaidOff => Balance.Grosze <= 0;

    /// <summary>Ile już spłacono, 0.0–1.0.</summary>
    public double Progress => InitialAmount.Grosze > 0
        ? Math.Clamp((double)(InitialAmount.Grosze - Balance.Grosze) / InitialAmount.Grosze, 0.0, 1.0)
        : (IsPaidOff ? 1.0 : 0.0);

    public Money PaidOff => new(Math.Max(0, InitialAmount.Grosze - Balance.Grosze));

    /// <summary>
    /// Ile rat zostało przy obecnym tempie. Null, gdy rata wynosi zero — wtedy
    /// dług nigdy się nie skończy i udawanie konkretnej liczby byłoby kłamstwem.
    /// </summary>
    public int? InstallmentsRemaining
    {
        get
        {
            if (IsPaidOff) return 0;
            if (InstallmentAmount.Grosze <= 0) return null;
            return (int)Math.Ceiling((double)Balance.Grosze / InstallmentAmount.Grosze);
        }
    }

    /// <summary>Przewidywany miesiąc wyjścia na zero przy obecnej racie.</summary>
    public DateOnly? ProjectedPayoff(DateOnly today)
    {
        var remaining = InstallmentsRemaining;
        if (remaining is null) return null;
        if (remaining == 0) return today;
        return new DateOnly(today.Year, today.Month, 1).AddMonths(remaining.Value);
    }
}
