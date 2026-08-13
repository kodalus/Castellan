using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class TransactionRepository(CastellanDbContext db) : ITransactionRepository
{
    public Task<Transaction?> GetAsync(TransactionId id, CancellationToken ct = default)
        => db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<Transaction>> ListForAccountAsync(
        AccountId accountId, CancellationToken ct = default)
        => await db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.OccurredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Transaction>> ListForMonthAsync(
        YearMonth month, CancellationToken ct = default)
    {
        // Month boundaries in local timezone (Europe/Warsaw)
        var localStart = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Local);
        var localEnd = localStart.AddMonths(1);
        var start = new DateTimeOffset(localStart);
        var end = new DateTimeOffset(localEnd);
        // EF Core SQLite can't translate DateTimeOffset range comparisons, filter client-side
        var all = await db.Transactions.ToListAsync(ct);
        return all
            .Where(t => t.OccurredAt >= start && t.OccurredAt < end)
            .OrderByDescending(t => t.OccurredAt)
            .ToList();
    }

    public Task AddAsync(Transaction transaction, CancellationToken ct = default)
    {
        db.Transactions.Add(transaction);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Transaction transaction, CancellationToken ct = default)
    {
        db.Transactions.Remove(transaction);
        return Task.CompletedTask;
    }
}
