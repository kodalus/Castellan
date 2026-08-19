using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Castellan.Domain.Tests;

/// <summary>
/// Fundusz otwarty — poduszka bezpieczeństwa. Ma cel, nie ma terminu, więc żadna
/// liczba wyprowadzana z terminu nie może zostać zmyślona: ani rata, ani opóźnienie.
/// </summary>
public class OpenEndedFundTest
{
    private static Fund Cushion() =>
        Fund.Create("Poduszka bezpieczeństwa", FundKind.Emergency, new Money(2_000_000), deadline: null);

    [Fact]
    public void Open_ended_fund_suggests_no_monthly_rate()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fund = Cushion();

        // Bez tego zadziałałaby gałąź „termin minął" i podpowiedziała całą brakującą
        // kwotę naraz — czyli 20 000 zł do wpłacenia w tym miesiącu.
        fund.SuggestedMonthly(today, paydateDay: 25).Grosze.Should().Be(0);
        fund.PeriodsRemaining(today, paydateDay: 25).Should().Be(0);
    }

    [Fact]
    public void Open_ended_fund_is_never_delayed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fund = Cushion();

        // Puste saldo przy celu 20 000 zł nie znaczy opóźnienia — nie ma tempa,
        // względem którego można być spóźnionym.
        fund.IsDelayed(today, paydateDay: 25).Should().BeFalse();
        fund.Deficit(today, paydateDay: 25).Grosze.Should().Be(0);
        fund.ExpectedByNow(today, paydateDay: 25).Grosze.Should().Be(0);
    }

    [Fact]
    public void Open_ended_fund_still_tracks_balance_and_progress()
    {
        var fund = Cushion();
        fund.Contribute(new Money(500_000));

        fund.Balance.Grosze.Should().Be(500_000);
        fund.Progress.Should().BeApproximately(0.25, 0.001);
        fund.Remaining.Grosze.Should().Be(1_500_000);
    }

    [Fact]
    public void A_fund_can_gain_or_lose_its_deadline_by_editing()
    {
        var fund = Cushion();
        var target = DateOnly.FromDateTime(DateTime.Today).AddMonths(12);

        fund.Update("Poduszka", FundKind.Emergency, new Money(2_000_000), target);
        fund.Deadline.Should().Be(new DateOnly(target.Year, target.Month, 1));

        fund.Update("Poduszka", FundKind.Emergency, new Money(2_000_000), null);
        fund.Deadline.Should().BeNull();
    }

    [Fact]
    public void A_dated_fund_keeps_computing_its_rate()
    {
        // Kontrola negatywna: zmiana nie może wyciszyć raty funduszom z terminem.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fund = Fund.Create("OC auto", FundKind.Insurance, new Money(120_000), today.AddMonths(6));

        fund.SuggestedMonthly(today, paydateDay: 0).Grosze.Should().BeGreaterThan(0);
    }
}
