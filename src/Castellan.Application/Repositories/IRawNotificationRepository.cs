using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IRawNotificationRepository
{
    Task AddAsync(RawNotification notification, CancellationToken ct = default);
    Task<IReadOnlyList<RawNotification>> ListUnparsedAsync(int limit = 200, CancellationToken ct = default);
    Task<int> CountByStatusAsync(ParseStatus status, CancellationToken ct = default);
}
