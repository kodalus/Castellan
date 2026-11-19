using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Repositories;

public interface ITransactionRepository
{
    Task<Transaction?> GetAsync(TransactionId id, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListForAccountAsync(AccountId accountId, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListForMonthAsync(YearMonth month, CancellationToken ct = default);

    /// <summary>Cała historia, od najnowszej. Dla szukania, które nie zna miesiąca.</summary>
    Task<IReadOnlyList<Transaction>> ListAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListUnsortedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListRecentAsync(DateTimeOffset since, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListProposedTransfersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListPaidFromFundAsync(FundId fundId, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListByTransferGroupAsync(Guid transferGroupId, CancellationToken ct = default);
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task RemoveAsync(Transaction transaction, CancellationToken ct = default);
}
