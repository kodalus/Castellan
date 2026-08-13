using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IReconciliationRepository
{
    Task<Reconciliation?> GetLatestForAccountAsync(AccountId accountId, CancellationToken ct = default);
    Task AddAsync(Reconciliation reconciliation, CancellationToken ct = default);
}
