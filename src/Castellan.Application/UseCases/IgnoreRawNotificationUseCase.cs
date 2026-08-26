using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Odkłada na bok powiadomienie, które nie opisuje transakcji. Brak powiadomienia nie
/// jest błędem — dwa szybkie tapnięcia w ten sam wiersz to zwykła sytuacja.
/// </summary>
public sealed class IgnoreRawNotificationUseCase(
    IRawNotificationRepository rawNotifications,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(RawNotificationId id, CancellationToken ct = default)
    {
        var notification = await rawNotifications.GetAsync(id, ct);
        if (notification is null) return;

        notification.Ignore();
        await uow.SaveChangesAsync(ct);
    }
}
