using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Repositories;

internal sealed class RawNotificationRepository(CastellanDbContext db) : IRawNotificationRepository
{
    public Task AddAsync(RawNotification notification, CancellationToken ct = default)
    {
        db.RawNotifications.Add(notification);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<RawNotification>> ListUnparsedAsync(int limit = 200, CancellationToken ct = default)
    {
        // EF Core SQLite can't translate DateTimeOffset ordering — load then sort client-side
        var all = await db.RawNotifications
            .Where(r => r.ParseStatus == ParseStatus.Unparsed)
            .ToListAsync(ct);
        return [.. all.OrderByDescending(r => r.PostedAt).Take(limit)];
    }

    public Task<int> CountByStatusAsync(ParseStatus status, CancellationToken ct = default)
        => db.RawNotifications.CountAsync(r => r.ParseStatus == status, ct);
}
