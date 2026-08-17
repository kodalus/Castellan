using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class MonthBudgetRepository(CastellanDbContext db) : IMonthBudgetRepository
{
    public Task<MonthBudget?> GetAsync(MonthBudgetId id, CancellationToken ct = default)
        => db.MonthBudgets
            .Include(b => b.Envelopes)
            .Include(b => b.IncomePlans)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<MonthBudget?> GetForMonthAsync(YearMonth month, CancellationToken ct = default)
        => db.MonthBudgets
            .Include(b => b.Envelopes)
            .Include(b => b.IncomePlans)
            .FirstOrDefaultAsync(b => b.Month == month, ct);

    public Task AddAsync(MonthBudget budget, CancellationToken ct = default)
    {
        db.MonthBudgets.Add(budget);
        return Task.CompletedTask;
    }
}
