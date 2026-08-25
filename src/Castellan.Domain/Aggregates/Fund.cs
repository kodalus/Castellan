using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

/// <summary>
/// Fundusz to koperta nad pieniędzmi, które fizycznie leżą na jakimś koncie:
/// <see cref="Contribute"/> podbija tylko własne saldo i nigdy nie rusza konta.
/// Dlatego salda funduszy NIE dolicza się do poduszki finansowej ani do wartości
/// netto — te pieniądze są tam już policzone w saldzie konta. Pieniądze poza
/// kontami znanymi aplikacji dodaje się jako aktywo, nie jako fundusz.
/// </summary>
public class Fund
{
    public FundId Id { get; private set; }
    public string Name { get; private set; } = "";
    public FundKind Kind { get; private set; }
    public Money TargetAmount { get; private set; }
    public DateOnly StartMonth { get; private set; }
    /// <summary>
    /// Brak terminu znaczy fundusz otwarty — zbierany, aż uzbiera. Tak działa
    /// poduszka bezpieczeństwa: ma cel, ale nie ma daty, na którą pieniądze muszą
    /// być gotowe. Bez terminu nie da się policzyć raty ani opóźnienia, więc oba
    /// wychodzą zerowe zamiast zmyślone.
    /// </summary>
    public DateOnly? Deadline { get; private set; }
    public Money Balance { get; private set; }
    public bool IsArchived { get; private set; }
    public DateOnly? LastContributionMonth { get; private set; }

    private Fund() { }

    public static Fund Create(string name, FundKind kind, Money targetAmount, DateOnly? deadline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new Fund
        {
            Id = FundId.New(),
            Name = name.Trim(),
            Kind = kind,
            TargetAmount = targetAmount,
            StartMonth = new DateOnly(today.Year, today.Month, 1),
            Deadline = FirstOfMonth(deadline),
            Balance = Money.Zero,
            IsArchived = false,
        };
    }

    public void Contribute(Money amount)
    {
        Balance = new Money(Balance.Grosze + amount.Grosze);
        var today = DateOnly.FromDateTime(DateTime.Today);
        LastContributionMonth = new DateOnly(today.Year, today.Month, 1);
    }

    public void Withdraw(Money amount) => Balance = new Money(Balance.Grosze - amount.Grosze);

    /// <summary>
    /// Korekta zebranej kwoty wprost z ekranu edycji — na wyrównanie z rzeczywistością,
    /// gdy saldo funduszu rozjechało się z tym, co realnie odłożone (odsetki na koncie,
    /// wpłata sprzed założenia funduszu, wpłata zapisana dwa razy).
    ///
    /// Celowo NIE rusza LastContributionMonth: poprawka to nie wpłata, a zaliczenie jej
    /// jako wpłaty zamknęłoby bieżący okres i podpowiadana rata zniknęłaby na miesiąc.
    /// </summary>
    public void SetBalance(Money balance)
    {
        if (balance.Grosze < 0)
            throw new ArgumentException("Zebrana kwota nie może być ujemna.", nameof(balance));
        Balance = balance;
    }
    public void Archive() => IsArchived = true;

    /// <summary>
    /// Edycja parametrów funduszu. Celowo nie rusza Balance ani StartMonth:
    /// saldo zmienia się tylko przez wpłaty/wypłaty, a StartMonth jest kotwicą
    /// dla wyliczeń "ile powinno być odłożone do teraz" — przesunięcie go
    /// zafałszowałoby historię opóźnień.
    /// </summary>
    public void Update(string name, FundKind kind, Money targetAmount, DateOnly? deadline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Kind = kind;
        TargetAmount = targetAmount;
        Deadline = FirstOfMonth(deadline);
    }

    private static DateOnly? FirstOfMonth(DateOnly? date) =>
        date is { } d ? new DateOnly(d.Year, d.Month, 1) : null;

    public Money Remaining => new(Math.Max(0, TargetAmount.Grosze - Balance.Grosze));

    public double Progress => TargetAmount.Grosze > 0
        ? Math.Min(1.0, (double)Balance.Grosze / TargetAmount.Grosze)
        : 0.0;

    // Upcoming paydates left to contribute before deadline.
    // Jeśli w tym miesiącu już wpłacono, bieżący okres liczy się jako zrobiony —
    // inaczej rata przeliczałaby się od nowa i pokazywała, że wciąż trzeba
    // dołożyć, mimo że wpłata już padła.
    public int PeriodsRemaining(DateOnly today, int paydateDay)
    {
        if (Deadline is not { } deadline) return 0;

        var from = HasContributedThisPeriod(today) ? StartOfNextMonth(today) : today;
        if (paydateDay <= 0)
            return Math.Max(0, (deadline.Year - from.Year) * 12 + (deadline.Month - from.Month) + 1);
        return UpcomingPaydates(from, paydateDay, deadline);
    }

    private bool HasContributedThisPeriod(DateOnly today) =>
        LastContributionMonth is { } m && m.Year == today.Year && m.Month == today.Month;

    private static DateOnly StartOfNextMonth(DateOnly today) =>
        today.Month == 12 ? new DateOnly(today.Year + 1, 1, 1) : new DateOnly(today.Year, today.Month + 1, 1);

    // Suggested contribution per next paycheck = Remaining / periods left
    public Money SuggestedMonthly(DateOnly today, int paydateDay)
    {
        // Fundusz otwarty nie podpowiada raty: bez terminu każda kwota jest tak samo
        // dobra, a wyrzucenie całej brakującej sumy (jak przy minionym terminie)
        // sugerowałoby, że trzeba ją wpłacić naraz.
        if (Deadline is null) return Money.Zero;

        var periods = PeriodsRemaining(today, paydateDay);
        if (periods <= 0) return Remaining;
        return new Money((long)Math.Ceiling((double)Remaining.Grosze / periods));
    }

    // What should have been saved by now, based only on PAST paydates.
    // If no paydate has passed since the fund was created, returns 0 (no false "delay").
    public Money ExpectedByNow(DateOnly today, int paydateDay)
    {
        // Bez terminu nie ma tempa, względem którego można być opóźnionym.
        if (Deadline is not { } deadline) return Money.Zero;

        int totalPeriods = paydateDay > 0
            ? UpcomingPaydates(StartMonth, paydateDay, deadline)
            : Math.Max(1, (deadline.Year - StartMonth.Year) * 12 + (deadline.Month - StartMonth.Month) + 1);

        if (totalPeriods <= 0) return TargetAmount;

        int elapsed = paydateDay > 0
            ? ElapsedPaydates(StartMonth, paydateDay, today)
            : Math.Max(0, (today.Year - StartMonth.Year) * 12 + (today.Month - StartMonth.Month));

        return new Money((long)Math.Round(TargetAmount.Grosze * (double)elapsed / totalPeriods));
    }

    public bool IsDelayed(DateOnly today, int paydateDay) =>
        Balance < ExpectedByNow(today, paydateDay);

    public Money Deficit(DateOnly today, int paydateDay)
    {
        var gap = ExpectedByNow(today, paydateDay).Grosze - Balance.Grosze;
        return new Money(Math.Max(0, gap));
    }

    // Paydates in [from, through] — paydate on deadline month counts if ≤ deadline day
    private static int UpcomingPaydates(DateOnly from, int paydateDay, DateOnly through)
    {
        var count = 0;
        var year  = from.Year;
        var month = from.Month;
        while (year < through.Year || (year == through.Year && month <= through.Month))
        {
            var day     = Math.Min(paydateDay, DateTime.DaysInMonth(year, month));
            var paydate = new DateOnly(year, month, day);
            if (paydate >= from && paydate <= through) count++;
            if (month == 12) { year++; month = 1; } else month++;
        }
        return count;
    }

    // Paydates in [from, before) — strictly before `before` (already past)
    private static int ElapsedPaydates(DateOnly from, int paydateDay, DateOnly before)
    {
        var count = 0;
        var year  = from.Year;
        var month = from.Month;
        while (year < before.Year || (year == before.Year && month <= before.Month))
        {
            var day     = Math.Min(paydateDay, DateTime.DaysInMonth(year, month));
            var paydate = new DateOnly(year, month, day);
            if (paydate >= from && paydate < before) count++;
            if (month == 12) { year++; month = 1; } else month++;
        }
        return count;
    }
}
