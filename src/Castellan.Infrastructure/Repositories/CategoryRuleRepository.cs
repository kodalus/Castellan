using Castellan.Application.Repositories;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

public class CategoryRuleRepository(CastellanDbContext db) : ICategoryRuleRepository
{
    public async Task<IReadOnlyList<CategoryRule>> ListAsync(CancellationToken ct = default)
        => await db.CategoryRules.OrderByDescending(r => r.HitCount).ThenBy(r => r.Pattern).ToListAsync(ct);

    public async Task AddAsync(CategoryRule rule, CancellationToken ct = default)
        => await db.CategoryRules.AddAsync(rule, ct);

    public Task RemoveAsync(CategoryRule rule, CancellationToken ct = default)
    {
        db.CategoryRules.Remove(rule);
        return Task.CompletedTask;
    }
}
