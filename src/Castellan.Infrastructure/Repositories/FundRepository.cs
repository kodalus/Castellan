using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class FundRepository(CastellanDbContext db) : IFundRepository
{
    public Task<Fund?> GetAsync(FundId id, CancellationToken ct = default)
        => db.Funds.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<Fund>> ListAsync(CancellationToken ct = default)
        => await db.Funds.OrderBy(f => f.Name).ToListAsync(ct);

    public Task AddAsync(Fund fund, CancellationToken ct = default)
    {
        db.Funds.Add(fund);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Fund fund, CancellationToken ct = default)
    {
        db.Funds.Remove(fund);
        return Task.CompletedTask;
    }
}
