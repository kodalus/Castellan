using Castellan.Domain.Exceptions;
using Castellan.Domain.ValueObjects;
using Castellan.Domain;

namespace Castellan.Domain.Aggregates;

public class Envelope
{
    public Guid Id { get; private set; }
    public MonthBudgetId MonthBudgetId { get; private set; }
    public CategoryId CategoryId { get; private set; }
    public Money PlannedAmount { get; private set; }

    private Envelope() { }

    internal Envelope(MonthBudgetId monthBudgetId, CategoryId categoryId, Money plannedAmount)
    {
        Id = Guid.CreateVersion7();
        MonthBudgetId = monthBudgetId;
        CategoryId = categoryId;
        PlannedAmount = plannedAmount;
    }

    internal void UpdateAmount(Money amount) => PlannedAmount = amount;
}

/// <summary>
/// Planowany przychód z danego źródła (kategorii przychodowej). Celowo osobna
/// encja, nie Envelope: koperty dzielą pulę AvailableFunds i podlegają limitowi
/// N-1, a plany przychodów są przewidywaniem wpływów — nie wolno ich wliczać
/// do tego samego limitu.
/// </summary>
public class IncomePlan
{
    public Guid Id { get; private set; }
    public MonthBudgetId MonthBudgetId { get; private set; }
    public CategoryId CategoryId { get; private set; }
    public Money PlannedAmount { get; private set; }

    private IncomePlan() { }

    internal IncomePlan(MonthBudgetId monthBudgetId, CategoryId categoryId, Money plannedAmount)
    {
        Id = Guid.CreateVersion7();
        MonthBudgetId = monthBudgetId;
        CategoryId = categoryId;
        PlannedAmount = plannedAmount;
    }

    internal void UpdateAmount(Money amount) => PlannedAmount = amount;
}

public class MonthBudget
{
    public MonthBudgetId Id { get; private set; }
    public YearMonth Month { get; private set; }
    public Money AvailableFunds { get; private set; }
    public DateTimeOffset PlannedAt { get; private set; }

    private readonly List<Envelope> _envelopes = [];
    public IReadOnlyList<Envelope> Envelopes => _envelopes.AsReadOnly();

    private readonly List<IncomePlan> _incomePlans = [];
    public IReadOnlyList<IncomePlan> IncomePlans => _incomePlans.AsReadOnly();

    public Money TotalPlannedIncome => new(_incomePlans.Sum(p => p.PlannedAmount.Grosze));

    private MonthBudget() { }

    public static MonthBudget Create(YearMonth month, Money availableFunds) =>
        new()
        {
            Id = MonthBudgetId.New(),
            Month = month,
            AvailableFunds = availableFunds,
            PlannedAt = DateTimeOffset.UtcNow,
        };

    // N-1: throws BudgetOverAllocatedException if total would exceed AvailableFunds
    public void Plan(CategoryId categoryId, Money amount)
    {
        if (amount < Money.Zero)
            throw new ArgumentException("Planned amount must be non-negative.", nameof(amount));

        var existing = _envelopes.FirstOrDefault(e => e.CategoryId == categoryId);
        var previous = existing?.PlannedAmount ?? Money.Zero;
        var currentTotal = new Money(_envelopes.Sum(e => e.PlannedAmount.Grosze));
        var newTotal = currentTotal - previous + amount;

        if (newTotal > AvailableFunds)
            throw new BudgetOverAllocatedException(newTotal, AvailableFunds);

        if (existing is not null)
            existing.UpdateAmount(amount);
        else
            _envelopes.Add(new Envelope(Id, categoryId, amount));
    }

    public void Remove(CategoryId categoryId)
    {
        var envelope = _envelopes.FirstOrDefault(e => e.CategoryId == categoryId);
        if (envelope is not null) _envelopes.Remove(envelope);
    }

    /// <summary>
    /// Plan przychodu z danego źródła. Bez limitu N-1 — to przewidywanie wpływów,
    /// a nie podział istniejącej puli.
    /// </summary>
    public void PlanIncome(CategoryId categoryId, Money amount)
    {
        if (amount < Money.Zero)
            throw new ArgumentException("Planned income must be non-negative.", nameof(amount));

        var existing = _incomePlans.FirstOrDefault(p => p.CategoryId == categoryId);
        if (existing is not null)
            existing.UpdateAmount(amount);
        else
            _incomePlans.Add(new IncomePlan(Id, categoryId, amount));
    }

    public void RemoveIncome(CategoryId categoryId)
    {
        var plan = _incomePlans.FirstOrDefault(p => p.CategoryId == categoryId);
        if (plan is not null) _incomePlans.Remove(plan);
    }

    public void RefreshAvailableFunds(Money funds) => AvailableFunds = funds;
}
