using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Castellan.Domain.Tests;

/// <summary>
/// Po terminie okresy przestają przybywać.
///
/// „Ile powinno być odłożone do teraz” liczy się jako cel × (minione wypłaty) /
/// (wypłaty do terminu). Po terminie licznik rośnie dalej, a mianownik stoi — więc
/// fundusz, na którym leży dokładnie cała kwota celu, zaczyna pokazywać brak,
/// i to tym większy, im dawniej termin minął. Fundusz OC z celem 1400 zł i zebranym
/// 1400 zł straszył „⚠ Brakuje 700,00 zł”: dokładnie połowę celu, bo minęła jedna
/// wypłata ponad dwie zaplanowane.
/// </summary>
public class FundAfterDeadlineTest
{
    private const long Cel = 140_000;

    /// <summary>
    /// StartMonth jest ustawiany na „teraz” i nie da się go cofnąć, więc zamiast
    /// przesuwać początek, przesuwamy dzień, o który pytamy.
    /// </summary>
    private static (Fund fund, DateOnly poTerminie) FundPoTerminie()
    {
        var dzis = DateOnly.FromDateTime(DateTime.Today);
        var poczatek = new DateOnly(dzis.Year, dzis.Month, 1);

        // Termin za dwa miesiące, pytamy o dzień pod koniec miesiąca terminu —
        // czyli po wypłacie, która już się w tym miesiącu wydarzyła.
        var termin = poczatek.AddMonths(2);
        var fund = Fund.Create("Ubezpieczenie OC", FundKind.Insurance, new Money(Cel), termin);

        return (fund, termin.AddDays(27));
    }

    [Fact]
    public void A_full_fund_past_its_deadline_is_not_behind()
    {
        var (fund, poTerminie) = FundPoTerminie();
        fund.Contribute(new Money(Cel));

        fund.ExpectedByNow(poTerminie, paydateDay: 10).Grosze
            .Should().Be(Cel, "więcej niż cel nigdy nie powinno być odłożone — to jest cel, a nie rata");
        fund.Deficit(poTerminie, paydateDay: 10).Grosze.Should().Be(0);
        fund.IsDelayed(poTerminie, paydateDay: 10).Should().BeFalse();
    }

    [Fact]
    public void Lowering_the_goal_to_what_is_already_saved_clears_the_shortfall()
    {
        // Dokładnie to, co zrobiła użytkowniczka: zebrane 1400, cel obniżony z 1500
        // do 1400 — po czym brak urósł do 700 zamiast zniknąć.
        var (fund, poTerminie) = FundPoTerminie();
        fund.Contribute(new Money(Cel));

        fund.Update("Ubezpieczenie OC", FundKind.Insurance, new Money(Cel), fund.Deadline);

        fund.Deficit(poTerminie, paydateDay: 10).Grosze.Should().Be(0);
    }

    [Fact]
    public void An_empty_fund_past_its_deadline_is_short_the_whole_goal()
    {
        // Kontrola negatywna: ucięcie oczekiwań na poziomie celu nie może wyciszyć
        // ostrzeżenia tam, gdzie pieniędzy naprawdę nie ma.
        var (fund, poTerminie) = FundPoTerminie();

        fund.Deficit(poTerminie, paydateDay: 10).Grosze.Should().Be(Cel);
        fund.IsDelayed(poTerminie, paydateDay: 10).Should().BeTrue();
    }

    [Fact]
    public void Before_the_deadline_the_pace_still_counts()
    {
        // Druga kontrola negatywna: w trakcie zbierania opóźnienie nadal ma się liczyć
        // proporcjonalnie, a nie skakać od razu do całego celu.
        var dzis = DateOnly.FromDateTime(DateTime.Today);
        var poczatek = new DateOnly(dzis.Year, dzis.Month, 1);
        var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(Cel), poczatek.AddMonths(4));

        var wTrakcie = poczatek.AddMonths(2).AddDays(27);

        var oczekiwane = fund.ExpectedByNow(wTrakcie, paydateDay: 10).Grosze;
        oczekiwane.Should().BeGreaterThan(0).And.BeLessThan(Cel);
    }
}
