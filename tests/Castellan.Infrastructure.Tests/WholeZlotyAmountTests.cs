using Castellan.Infrastructure.Parsers;
using FluentAssertions;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Kwoty bez groszy. Banki piszą „Wydano 9 zł.", nie „9,00 zł" — a wzorce wymagały
/// przecinka i dwóch cyfr po nim. Skutek w Revolucie był zdradliwy: wzorzec nie
/// dopasowywał się do kwoty transakcji, więc pierwszą pasującą liczbą w treści
/// stawało się SALDO KONTA z drugiej linii i to ono lądowało jako kwota wydatku.
/// </summary>
public class WholeZlotyAmountTests
{
    [Fact]
    public void Revolut_takes_the_amount_not_the_balance_when_there_are_no_grosze()
    {
        var parser = new RevolutNotificationParser();

        var result = parser.TryParse(
            title: "Konto wspólne · Piekarnia 57498",
            text: "Wydano 9 zł.\n⚠ Saldo konta „PLN”: 41,71 zł.");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-900, "wydano 9 zł, a nie tyle, ile zostało na koncie");
        result.Merchant.Should().Be("Piekarnia 57498");
    }

    [Fact]
    public void Revolut_still_prefers_the_first_amount_when_grosze_are_present()
    {
        // Kontrola negatywna: rozluźnienie wzorca nie może zepsuć przypadku z groszami.
        var parser = new RevolutNotificationParser();

        var result = parser.TryParse(
            title: "Konto wspólne · Lidl",
            text: "Wydano 136,39 zł.\nSaldo konta „PLN”: 457,38 zł.");

        result!.Amount.Grosze.Should().Be(-13639);
    }

    [Fact]
    public void Ing_parses_a_whole_zloty_amount()
    {
        // Tu skutek był inny niż w Revolucie: wzorzec nie pasował wcale, więc
        // transakcja nie powstawała.
        var parser = new IngNotificationParser();

        var result = parser.TryParse(
            title: "Moje ING. Twój Asystent",
            text: "9 PLN mniej na Twoim koncie - Piekarnia");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-900);
        result.Merchant.Should().Be("Piekarnia");
    }

    [Fact]
    public void Google_wallet_parses_a_whole_zloty_amount()
    {
        var parser = new GoogleWalletNotificationParser();

        var result = parser.TryParse(
            title: "PIEKARNIA",
            text: "Kwota 9 zł – karta Revolut Wspólny");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-900);
    }
}
