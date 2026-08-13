using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface ICategoryRuleRepository
{
    Task<IReadOnlyList<CategoryRule>> ListAsync(CancellationToken ct = default);
    Task AddAsync(CategoryRule rule, CancellationToken ct = default);
    Task RemoveAsync(CategoryRule rule, CancellationToken ct = default);
}
