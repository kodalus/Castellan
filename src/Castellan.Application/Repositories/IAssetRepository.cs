using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IAssetRepository
{
    Task<Asset?> GetAsync(AssetId id, CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Asset asset, CancellationToken ct = default);
}
