using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.Repositories;

public interface IRawNotificationRepository
{
    Task AddAsync(RawNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Czy to samo powiadomienie juz zostalo zapisane. Android potrafi doreczyc jedno
    /// powiadomienie wiecej niz raz — przy odswiezeniu tresci przez apke banku albo gdy
    /// nasluch jest podpiety dwa razy po aktualizacji aplikacji. Porownujemy tresc razem
    /// ze znacznikiem czasu z systemu: dwie ROZNE platnosci nie trafia w te sama
    /// milisekunde z identycznym tekstem.
    /// </summary>
    Task<bool> ExistsAsync(
        string packageName, string title, string text, DateTimeOffset postedAt,
        CancellationToken ct = default);
    Task<RawNotification?> GetAsync(RawNotificationId id, CancellationToken ct = default);
    Task<IReadOnlyList<RawNotification>> ListUnparsedAsync(int limit = 200, CancellationToken ct = default);
    Task<int> CountByStatusAsync(ParseStatus status, CancellationToken ct = default);

    /// <summary>
    /// Powiadomienia, z których powstała transakcja. Zachowana treść pozwala odtworzyć
    /// odczyt po poprawce wzorca i porównać go z tym, co wtedy zapisano.
    /// </summary>
    Task<IReadOnlyList<RawNotification>> ListParsedAsync(CancellationToken ct = default);

    /// <summary>
    /// Sparsowane powiadomienia z okna czasowego. Deduplikacja potrzebuje z nich
    /// nazwy pakietu, żeby odróżnić „ta sama płatność zgłoszona przez dwie aplikacje"
    /// od „dwie różne płatności na tę samą kwotę".
    /// </summary>
    Task<IReadOnlyList<RawNotification>> ListParsedSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}
