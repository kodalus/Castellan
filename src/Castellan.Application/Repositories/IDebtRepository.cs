using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IDebtRepository
{
    Task<Debt?> GetAsync(DebtId id, CancellationToken ct = default);
    Task<IReadOnlyList<Debt>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Debt debt, CancellationToken ct = default);
    Task RemoveAsync(Debt debt, CancellationToken ct = default);
}
