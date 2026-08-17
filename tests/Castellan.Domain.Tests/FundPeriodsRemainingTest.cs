using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Castellan.Domain.Tests;

/// <summary>
/// Wpłata w tym miesiącu musi wyłączyć bieżący okres z wyliczeń "ile jeszcze
/// zostało" — inaczej rata przeliczałaby się na nowo od razu po wpłacie i
/// pokazywała, że wciąż trzeba dołożyć, mimo że pieniądze już poszły do funduszu.
/// </summary>
public class FundPeriodsRemainingTest
{
    [Fact]
    public void Contributing_today_excludes_the_current_paydate_from_periods_remaining()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_200_000), today.AddMonths(12));

        var before = fund.PeriodsRemaining(today, paydateDay: 31);
        fund.Contribute(new Money(100_000));
        var after = fund.PeriodsRemaining(today, paydateDay: 31);

        after.Should().Be(before - 1, "wpłata dziś pokrywa bieżący okres — nie liczy się drugi raz");
    }

    [Fact]
    public void Suggested_monthly_after_contributing_reflects_only_future_periods()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        // 12 okresów po 100 000 do zebrania.
        var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_200_000), today.AddMonths(12));

        fund.Contribute(new Money(100_000));

        var periods = fund.PeriodsRemaining(today, paydateDay: 31);
        var suggested = fund.SuggestedMonthly(today, paydateDay: 31);

        // Zostało 1 100 000 do zebrania w 11 przyszłych okresach (bieżący już opłacony).
        periods.Should().Be(11);
        suggested.Grosze.Should().Be(100_000, "po wpłacie równej racie kolejne raty nie powinny się zmienić");
    }

    [Fact]
    public void Without_a_contribution_this_month_the_current_period_still_counts()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(1_200_000), today.AddMonths(12));

        // Brak wpłaty — bieżący okres nadal jest do zrobienia.
        fund.PeriodsRemaining(today, paydateDay: 31).Should().Be(12);
    }
}
