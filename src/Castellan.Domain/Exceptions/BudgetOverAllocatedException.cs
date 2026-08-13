using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Exceptions;

public sealed class BudgetOverAllocatedException(Money attempted, Money available)
    : InvalidOperationException(
        $"Budget overallocated: attempted {attempted}, available {available}.")
{
    public Money Attempted { get; } = attempted;
    public Money Available { get; } = available;
}
