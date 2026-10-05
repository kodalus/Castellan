using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IAccountRepository
{
    Task<Account?> GetAsync(AccountId id, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Account account, CancellationToken ct = default);
    Task RemoveAsync(Account account, CancellationToken ct = default);
}
