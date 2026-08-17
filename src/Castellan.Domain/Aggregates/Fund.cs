using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Fund
{
    public FundId Id { get; private set; }
    public string Name { get; private set; } = "";
    public FundKind Kind { get; private set; }
    public Money TargetAmount { get; private set; }
    public DateOnly StartMonth { get; private set; }
    public DateOnly Deadline { get; private set; }
    public Money Balance { get; private set; }
    public bool IsArchived { get; private set; }
    public DateOnly? LastContributionMonth { get; private set; }

    private Fund() { }

    public static Fund Create(string name, FundKind kind, Money targetAmount, DateOnly deadline)
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
            Deadline = new DateOnly(deadline.Year, deadline.Month, 1),
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
    public void Archive() => IsArchived = true;

    /// <summary>
    /// Edycja parametrów funduszu. Celowo nie rusza Balance ani StartMonth:
    /// saldo zmienia się tylko przez wpłaty/wypłaty, a StartMonth jest kotwicą
    /// dla wyliczeń "ile powinno być odłożone do teraz" — przesunięcie go
    /// zafałszowałoby historię opóźnień.
    /// </summary>
    public void Update(string name, FundKind kind, Money targetAmount, DateOnly deadline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Kind = kind;
        TargetAmount = targetAmount;
        Deadline = new DateOnly(deadline.Year, deadline.Month, 1);
    }

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
        var from = HasContributedThisPeriod(today) ? StartOfNextMonth(today) : today;
        if (paydateDay <= 0)
            return Math.Max(0, (Deadline.Year - from.Year) * 12 + (Deadline.Month - from.Month) + 1);
        return UpcomingPaydates(from, paydateDay, Deadline);
    }

    private bool HasContributedThisPeriod(DateOnly today) =>
        LastContributionMonth is { } m && m.Year == today.Year && m.Month == today.Month;

    private static DateOnly StartOfNextMonth(DateOnly today) =>
        today.Month == 12 ? new DateOnly(today.Year + 1, 1, 1) : new DateOnly(today.Year, today.Month + 1, 1);

    // Suggested contribution per next paycheck = Remaining / periods left
    public Money SuggestedMonthly(DateOnly today, int paydateDay)
    {
        var periods = PeriodsRemaining(today, paydateDay);
        if (periods <= 0) return Remaining;
        return new Money((long)Math.Ceiling((double)Remaining.Grosze / periods));
    }

    // What should have been saved by now, based only on PAST paydates.
    // If no paydate has passed since the fund was created, returns 0 (no false "delay").
    public Money ExpectedByNow(DateOnly today, int paydateDay)
    {
        int totalPeriods = paydateDay > 0
            ? UpcomingPaydates(StartMonth, paydateDay, Deadline)
            : Math.Max(1, (Deadline.Year - StartMonth.Year) * 12 + (Deadline.Month - StartMonth.Month) + 1);

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
