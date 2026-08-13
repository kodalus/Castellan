using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Repositories;

public interface IMonthBudgetRepository
{
    Task<MonthBudget?> GetAsync(MonthBudgetId id, CancellationToken ct = default);
    Task<MonthBudget?> GetForMonthAsync(YearMonth month, CancellationToken ct = default);
    Task AddAsync(MonthBudget budget, CancellationToken ct = default);
}
