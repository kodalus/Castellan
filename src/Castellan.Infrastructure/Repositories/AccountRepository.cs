using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class AccountRepository(CastellanDbContext db) : IAccountRepository
{
    public Task<Account?> GetAsync(AccountId id, CancellationToken ct = default)
        => db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default)
        => await db.Accounts
            .Where(a => !a.IsArchived)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

    public Task AddAsync(Account account, CancellationToken ct = default)
    {
        db.Accounts.Add(account);
        return Task.CompletedTask;
    }
}
