using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class DebtRepository(CastellanDbContext db) : IDebtRepository
{
    public Task<Debt?> GetAsync(DebtId id, CancellationToken ct = default)
        => db.Debts.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<Debt>> ListAsync(CancellationToken ct = default)
        => await db.Debts.OrderBy(d => d.Name).ToListAsync(ct);

    public Task AddAsync(Debt debt, CancellationToken ct = default)
    {
        db.Debts.Add(debt);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Debt debt, CancellationToken ct = default)
    {
        db.Debts.Remove(debt);
        return Task.CompletedTask;
    }
}
