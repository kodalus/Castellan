using System.Globalization;
using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Domain.ValueObjects;

namespace Castellan.Infrastructure.Parsers;

public sealed partial class RevolutNotificationParser : INotificationParser
{
    public string PackageName => "com.revolut.revolut";

    // Matches: "PLN 123.45", "-EUR 1,234.56", "USD 0.99"
    [GeneratedRegex(@"([+-]?)\s*(?:PLN|EUR|USD|GBP|CHF)\s*([\d,]+\.\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountCurrencyFirst();

    // Matches: "123.45 PLN", "+1,234.56 EUR"
    [GeneratedRegex(@"([+-]?)\s*([\d,]+\.\d{2})\s*(?:PLN|EUR|USD|GBP|CHF)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountCurrencyLast();

    private static readonly string[] IncomeWords =
        ["received", "refund", "cashback", "topped up", "added", "money in"];
    private static readonly string[] ExpenseWords =
        ["paid", "payment", "sent", "withdrawn", "charge", "declined", "money out"];

    public ParsedTransaction? TryParse(string title, string text)
    {
        var combined = $"{title} {text}";
        var m = AmountCurrencyFirst().Match(combined);
        if (!m.Success) m = AmountCurrencyLast().Match(combined);
        if (!m.Success) return null;

        var signStr = m.Groups[1].Value;
        var amountStr = m.Groups[2].Value.Replace(",", "");
        if (!decimal.TryParse(amountStr, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

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
