using System.Globalization;
using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Domain.ValueObjects;

namespace Castellan.Infrastructure.Parsers;

public sealed partial class IngNotificationParser : INotificationParser
{
    public string PackageName => "pl.ing.mojeing";

    // Matches Polish-format amounts: "345,67 PLN", "1 234,56 zł", "-100,00 PLN"
    [GeneratedRegex(@"([+-]?)\s*((?:\d[\d ]*)\d|\d),(\d{2})\s*(?:PLN|zł)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PolishAmount();

    private static readonly string[] IncomeWords =
        ["otrzymał", "przychodzący", "wpłata", "zwrot", "odsetki"];
    private static readonly string[] ExpenseWords =
        ["zapłacił", "wychodzący", "wypłata", "zakup", "obciążen", "płatność"];

    public ParsedTransaction? TryParse(string title, string text)
    {
        var combined = $"{title} {text}";
        var m = PolishAmount().Match(combined);
        if (!m.Success) return null;

        var intPart = m.Groups[2].Value.Replace(" ", "");
        var decPart = m.Groups[3].Value;
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

        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero) * sign;
        var merchant = text.Length > 80 ? text[..80] : text;
        return new ParsedTransaction(new Money(grosze), merchant.Trim());
    }
}
