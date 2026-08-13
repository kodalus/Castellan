using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface ICategoryRepository
{
    Task<Category?> GetAsync(CategoryId id, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetManyAsync(IEnumerable<CategoryId> ids, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
}
