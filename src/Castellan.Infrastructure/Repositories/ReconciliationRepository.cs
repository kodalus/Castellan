using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class ReconciliationRepository(CastellanDbContext db) : IReconciliationRepository
{
    public async Task<Reconciliation?> GetLatestForAccountAsync(AccountId accountId, CancellationToken ct = default)
    {
        // EF Core SQLite can't translate DateTimeOffset ordering — sort client-side
        var all = await db.Reconciliations
            .Where(r => r.AccountId == accountId)
            .ToListAsync(ct);
        return all.MaxBy(r => r.ObservedAt);
    }

    public Task AddAsync(Reconciliation reconciliation, CancellationToken ct = default)
    {
        db.Reconciliations.Add(reconciliation);
        return Task.CompletedTask;
    }
}
