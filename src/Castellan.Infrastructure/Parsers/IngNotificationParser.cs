using System.Globalization;
using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Domain.ValueObjects;

namespace Castellan.Infrastructure.Parsers;

public sealed partial class IngNotificationParser : INotificationParser
{
    public string PackageName => "pl.ing.mojeing";

    // "Twój Asystent" format:
    // Text: "69,57 PLN mniej na Twoim koncie - Direct Rika - płatność BLIK"
    // Text: "1 500,00 PLN więcej na Twoim koncie - Jan Kowalski - przelew przychodzący"
    // Text: "345,67 PLN mniej na Twoim koncie"  (no merchant segment)
    // Text: "1 600,00 PLN więcej na Twoim koncie - Direct Rika"  (merchant, no suffix)
    // Ogon jest rozbity na dwie osobne opcjonalne grupy, bo wariant z samą nazwą
    // (jeden myślnik) wcześniej nie pasował do niczego i spadał do parsera ogólnego,
    // który za sprzedawcę bierze całą treść powiadomienia.
    [GeneratedRegex(
        @"^(\d[\d ]*)(?:,(\d{2}))?\s+PLN\s+(mniej|więcej)\s+na\s+Twoim\s+koncie(?:\s*-\s*(.+?))?(?:\s*-\s*.+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssistantPattern();

    // Generic fallback: matches amounts anywhere in title+text
    [GeneratedRegex(@"([+-]?)\s*((?:\d[\d ]*)\d|\d)(?:,(\d{2}))?\s*(?:PLN|zł)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PolishAmount();

    private static readonly string[] IncomeWords =
        ["otrzymał", "przychodzący", "wpłata", "zwrot", "odsetki", "więcej"];
    private static readonly string[] ExpenseWords =
        ["zapłacił", "wychodzący", "wypłata", "zakup", "obciążen", "płatność", "mniej"];

    public ParsedTransaction? TryParse(string title, string text)
    {
        title = NotificationText.Normalize(title);
        text  = NotificationText.Normalize(text);

        // Try specific "Twój Asystent" format first — gives clean merchant extraction
        if (title.Contains("Asystent", StringComparison.OrdinalIgnoreCase))
        {
            var result = TryParseAssistant(text);
            if (result is not null) return result;
        }

        // Fall back to generic keyword-based parsing
        return TryParseGeneric(title, text);
    }

    private static ParsedTransaction? TryParseAssistant(string text)
    {
        var m = AssistantPattern().Match(text.Trim());
        if (!m.Success) return null;

        var intPart = m.Groups[1].Value.Replace(" ", "");
        var decPart = m.Groups[2].Success ? m.Groups[2].Value : "00";
        if (!decimal.TryParse($"{intPart}.{decPart}", NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

        var sign = m.Groups[3].Value.Equals("mniej", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero) * sign;

        // Group 4 = merchant (optional — absent when "mniej na Twoim koncie" has no " - X - Y" suffix)
        var merchant = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null;

        return new ParsedTransaction(new Money(grosze), merchant);
    }

    private static ParsedTransaction? TryParseGeneric(string title, string text)
    {
        var combined = $"{title} {text}";
        var m = PolishAmount().Match(combined);
        if (!m.Success) return null;

        var intPart = m.Groups[2].Value.Replace(" ", "");
        var decPart = m.Groups[3].Success ? m.Groups[3].Value : "00";
        if (!decimal.TryParse($"{intPart}.{decPart}", NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

        var signStr = m.Groups[1].Value;
        var lower = combined.ToLowerInvariant();
        int sign;
        if (signStr == "-")
            sign = -1;
        else if (signStr == "+")
            sign = 1;
        else if (IncomeWords.Any(lower.Contains))
            sign = 1;
        else if (ExpenseWords.Any(lower.Contains))
            sign = -1;
        else
            return null;

        var merchant = text.Length > 80 ? text[..80] : text;
        return new ParsedTransaction(new Money((long)Math.Round(dec * 100) * sign), merchant.Trim());
    }
}
