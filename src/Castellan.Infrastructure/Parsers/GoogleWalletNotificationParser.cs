using System.Globalization;
using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Domain.ValueObjects;

namespace Castellan.Infrastructure.Parsers;

/// <summary>
/// Płatność telefonem NFC bywa jedynym śladem transakcji — bank nie zawsze
/// wysyła własne powiadomienie dla płatności zbliżeniowej z Portfela Google
/// (obserwowane dla kart ING). Tytuł = sprzedawca ("LIDL 2306"),
/// treść = "Kwota 136,39 zł – karta Revolut Wspólny".
/// </summary>
public sealed partial class GoogleWalletNotificationParser : INotificationParser
{
    public string PackageName => "com.google.android.apps.walletnfcrel";

    [GeneratedRegex(
        @"Kwota\s+(\d[\d ]*),(\d{2})\s*zł(?:.*?karta\s+(.+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex KwotaPattern();

    public ParsedTransaction? TryParse(string title, string text)
    {
        title = NotificationText.Normalize(title);
        text  = NotificationText.Normalize(text);

        var m = KwotaPattern().Match(text);
        if (!m.Success) return null;

        var intPart = m.Groups[1].Value.Replace(" ", "");
        var decPart = m.Groups[2].Value;
        if (!decimal.TryParse($"{intPart}.{decPart}", NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

        // Zbliżeniowa płatność telefonem to zawsze wydatek — Portfel Google
        // nie powiadamia o zwrotach czy wpłatach.
        var grosze = -(long)Math.Round(dec * 100, MidpointRounding.AwayFromZero);

        var accountHint = m.Groups[3].Success ? m.Groups[3].Value.Trim() : null;
        var merchant = title.Trim();

        return new ParsedTransaction(new Money(grosze), merchant, accountHint);
    }
}
