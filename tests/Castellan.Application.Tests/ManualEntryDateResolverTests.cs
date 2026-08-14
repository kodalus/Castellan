using Castellan.Application.Services;
using FluentAssertions;

namespace Castellan.Application.Tests;

/// <summary>
/// Reguluje regresję: ekran ręcznego dodawania transakcji wysyłał OccurredAt jako
/// północ wybranego dnia. Konto założone dziś po południu ma LastReconciledAt =
/// moment założenia; transakcja z domyślną datą "dziś" dostawała północ — czyli
/// czas WCZEŚNIEJSZY niż uzgodnienie — więc GetAccountsWithBalancesUseCase (filtr
/// OccurredAt > LastReconciledAt) po cichu ją pomijał. Transakcja była zapisana
/// i widoczna na liście, ale nigdy nie schodziła z salda konta.
/// </summary>
public class ManualEntryDateResolverTests
{
    [Fact]
    public void Today_resolves_to_the_current_moment_not_midnight()
    {
        var now = new DateTimeOffset(2026, 8, 14, 17, 30, 0, TimeSpan.FromHours(2));

        var resolved = ManualEntryDateResolver.Resolve(pickedDate: now.Date, now);

        resolved.Should().Be(now);
    }

    [Fact]
    public void Today_stays_after_an_account_reconciled_earlier_the_same_afternoon()
    {
        // Konto założone o 15:00 (LastReconciledAt); transakcja dodana o 17:30 tego
        // samego dnia z domyślną datą "dziś" — musi wypaść PO uzgodnieniu.
        var reconciledAt = new DateTimeOffset(2026, 8, 14, 15, 0, 0, TimeSpan.FromHours(2));
        var addedAt = new DateTimeOffset(2026, 8, 14, 17, 30, 0, TimeSpan.FromHours(2));

        var resolved = ManualEntryDateResolver.Resolve(pickedDate: addedAt.Date, addedAt);

        (resolved > reconciledAt).Should().BeTrue();
    }

    [Fact]
    public void Backdated_entry_resolves_to_end_of_the_picked_day()
    {
        var now = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.FromHours(2));
        var pickedDate = new DateTime(2026, 8, 10);

        var resolved = ManualEntryDateResolver.Resolve(pickedDate, now);

        resolved.Should().Be(new DateTimeOffset(2026, 8, 10, 23, 59, 59, TimeSpan.FromHours(2)));
    }
}
