using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class CategoryRepository(CastellanDbContext db) : ICategoryRepository
{
    public Task<Category?> GetAsync(CategoryId id, CancellationToken ct = default)
        => db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct = default)
        => await db.Categories.OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Category>> GetManyAsync(
        IEnumerable<CategoryId> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        return await db.Categories.Where(c => list.Contains(c.Id)).ToListAsync(ct);
    }

    public Task AddAsync(Category category, CancellationToken ct = default)
    {
        db.Categories.Add(category);
        return Task.CompletedTask;
    }
}
