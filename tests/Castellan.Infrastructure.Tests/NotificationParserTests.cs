using Castellan.Infrastructure.Parsers;
using FluentAssertions;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Reguluje regresję: parser Revolut był napisany pod anglojęzyczny format
/// ("PLN 123.45"), ale realna polska aplikacja wysyła "Wydano 136,39 zł." —
/// żaden dotychczasowy wzorzec tego nie łapał, więc transakcje NFC nie były
/// przechwytywane wcale. Teksty w testach to dosłowne treści z ekranu telefonu.
/// </summary>
public class NotificationParserTests
{
    [Fact]
    public void Revolut_parses_polish_expense_notification()
    {
        var parser = new RevolutNotificationParser();

        var result = parser.TryParse(
            title: "Konto wspólne · Lidl",
            text: "Wydano 136,39 zł.\nSaldo konta „PLN”: 457,38 zł.");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-13639);
        result.Merchant.Should().Be("Lidl");
    }

    [Fact]
    public void Revolut_ignores_balance_amount_and_uses_the_transaction_amount()
    {
        // "Saldo konta" (457,38 zł) comes after "Wydano" (136,39 zł) in the text —
        // the parser must pick the first (transaction) amount, not the balance.
        var parser = new RevolutNotificationParser();

        var result = parser.TryParse(
            title: "Konto wspólne · Lidl",
            text: "Wydano 136,39 zł.\nSaldo konta „PLN”: 457,38 zł.");

        result!.Amount.Grosze.Should().NotBe(-45738);
    }

    [Fact]
    public void Revolut_still_parses_english_format_for_backward_compatibility()
    {
        var parser = new RevolutNotificationParser();

        var result = parser.TryParse(title: "Payment", text: "You paid PLN 45.00 at Zabka");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-4500);
    }

    [Fact]
    public void GoogleWallet_parses_nfc_payment_and_extracts_account_hint()
    {
        var parser = new GoogleWalletNotificationParser();

        var result = parser.TryParse(
            title: "LIDL 2306",
            text: "Kwota 136,39 zł – karta Revolut Wspólny");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-13639);
        result.Merchant.Should().Be("LIDL 2306");
        result.AccountHint.Should().Be("Revolut Wspólny");
    }

    [Fact]
    public void GoogleWallet_still_parses_amount_when_card_name_is_absent()
    {
        var parser = new GoogleWalletNotificationParser();

        var result = parser.TryParse(title: "Zabka 123", text: "Kwota 12,50 zł");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-1250);
        result.AccountHint.Should().BeNull();
    }

    [Fact]
    public void Revolut_parses_expense_made_by_another_person_on_a_shared_account()
    {
        // Konto wspolne: gdy placi wspoluzytkownik, Revolut pisze
        // "Sylwester Rzepka wydal(a) 11,13 zl." zamiast "Wydano 11,13 zl.".
        // Slownik znal tylko forme bezosobowa, wiec parser zwracal null
        // i takie zakupy nie trafialy do skrzynki wcale.
        var parser = new RevolutNotificationParser();

        var result = parser.TryParse(
            title: "Konto wspólne · Sklep Firmowy Wiece",
            text: "Sylwester Rzepka wydał(a) 11,13 zł.\nSaldo konta „PLN”: 192,64 zł.");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-1113);
        result.Merchant.Should().Be("Sklep Firmowy Wiece");
    }

    [Fact]
    public void Ing_parses_amount_with_non_breaking_thousands_separator()
    {
        // Polskie formatowanie kwot uzywa TWARDEJ spacji (U+00A0) miedzy tysiacami.
        // Wzorce akceptowaly tylko zwykla spacje, wiec "1<NBSP>600,00" bylo czytane
        // od drugiej czesci: 600 zamiast 1600.
        var parser = new IngNotificationParser();

        var result = parser.TryParse(
            title: "Moje ING. Twój Asystent",
            text: "1 600,00 PLN więcej na Twoim koncie - Direct Rika");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(160_000);
        // Jeden mysalnik zamiast dwoch: wczesniej wzorzec "Asystenta" nie pasowal
        // i wpis spadal do parsera ogolnego, ktory za sprzedawce bierze cala tresc.
        result.Merchant.Should().Be("Direct Rika");
    }

    [Fact]
    public void Ing_still_extracts_merchant_when_the_suffix_is_present()
    {
        // Kontrola negatywna dla rozluznionego wzorca: wariant z dwoma myslnikami
        // ma dalej dawac sama nazwe, bez doklejonego ogona.
        var parser = new IngNotificationParser();

        var result = parser.TryParse(
            title: "Moje ING. Twój Asystent",
            text: "69,57 PLN mniej na Twoim koncie - Direct Rika - płatność BLIK");

        result.Should().NotBeNull();
        result!.Amount.Grosze.Should().Be(-6957);
        result.Merchant.Should().Be("Direct Rika");
    }
}
