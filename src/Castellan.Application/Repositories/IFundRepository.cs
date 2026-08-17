using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IFundRepository
{
    Task<Fund?> GetAsync(FundId id, CancellationToken ct = default);
    Task<IReadOnlyList<Fund>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Fund fund, CancellationToken ct = default);
    Task RemoveAsync(Fund fund, CancellationToken ct = default);
}
