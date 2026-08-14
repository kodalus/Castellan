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
}
