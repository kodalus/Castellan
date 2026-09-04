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

    public async Task<bool> ExistsAsync(
        string packageName, string title, string text, DateTimeOffset postedAt,
        CancellationToken ct = default)
    {
        // Po stronie SQLite tylko tekst — porownanie DateTimeOffset zostaje w pamieci,
        // bo EF Core nie tlumaczy go na SQL (patrz ListUnparsedAsync).
        var sameContent = await db.RawNotifications
            .Where(r => r.PackageName == packageName && r.Title == title && r.Text == text)
            .ToListAsync(ct);

        return sameContent.Any(r => r.PostedAt == postedAt);
    }

    public async Task<RawNotification?> GetAsync(RawNotificationId id, CancellationToken ct = default) =>
        await db.RawNotifications.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<RawNotification>> ListUnparsedAsync(int limit = 200, CancellationToken ct = default)
    {
        // EF Core SQLite can't translate DateTimeOffset ordering — load then sort client-side
        var all = await db.RawNotifications
            .Where(r => r.ParseStatus == ParseStatus.Unparsed)
            .ToListAsync(ct);
        return [.. all.OrderByDescending(r => r.PostedAt).Take(limit)];
    }

    public async Task<IReadOnlyList<RawNotification>> ListParsedAsync(CancellationToken ct = default)
    {
        var all = await db.RawNotifications
            .Where(r => r.ParseStatus == ParseStatus.Parsed && r.TransactionId != null)
            .ToListAsync(ct);
        return [.. all.OrderByDescending(r => r.PostedAt)];
    }

    public async Task<IReadOnlyList<RawNotification>> ListParsedSinceAsync(
        DateTimeOffset since, CancellationToken ct = default)
    {
        var all = await db.RawNotifications
            .Where(r => r.ParseStatus == ParseStatus.Parsed && r.TransactionId != null)
            .ToListAsync(ct);
        return [.. all.Where(r => r.PostedAt >= since)];
    }

    public Task<int> CountByStatusAsync(ParseStatus status, CancellationToken ct = default)
        => db.RawNotifications.CountAsync(r => r.ParseStatus == status, ct);
}
