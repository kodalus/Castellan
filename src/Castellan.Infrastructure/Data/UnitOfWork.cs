using Castellan.Application;

namespace Castellan.Infrastructure.Data;

internal sealed class UnitOfWork(CastellanDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
