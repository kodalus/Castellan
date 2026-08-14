using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class AssetRepository(CastellanDbContext db) : IAssetRepository
{
    public Task<Asset?> GetAsync(AssetId id, CancellationToken ct = default) =>
        db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Asset>> ListAsync(CancellationToken ct = default) =>
        await db.Assets
            .OrderBy(a => a.Liquidity)
            .ThenBy(a => a.Name)
            .ToListAsync(ct);

    public Task AddAsync(Asset asset, CancellationToken ct = default)
    {
        db.Assets.Add(asset);
        return Task.CompletedTask;
    }
}
