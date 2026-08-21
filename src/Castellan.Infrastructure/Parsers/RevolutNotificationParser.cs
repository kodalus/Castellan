using System.Globalization;
using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Domain.ValueObjects;

namespace Castellan.Infrastructure.Parsers;

public sealed partial class RevolutNotificationParser : INotificationParser
{
    public string PackageName => "com.revolut.revolut";

    // Format polski (domyślny w aplikacji): "Wydano 136,39 zł.", "Otrzymano 50,00 zł."
    // Tytuł: "Konto wspólne · Lidl" — segment po " · " to nazwa odbiorcy/sprzedawcy.
    // Grosze są opcjonalne: bank pisze „Wydano 9 zł.", nie „9,00 zł". Gdy wzorzec
    // ich wymagał, kwota transakcji nie pasowała i pierwszą pasującą liczbą w treści
    // stawało się SALDO KONTA z drugiej linii — to ono lądowało jako kwota wydatku.
    [GeneratedRegex(@"(\d[\d ]*)(?:,(\d{2}))?\s*zł", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PolishAmount();

    // Format angielski/międzynarodowy — starszy wariant apki: "PLN 123.45", "-EUR 1,234.56"
    [GeneratedRegex(@"([+-]?)\s*(?:PLN|EUR|USD|GBP|CHF)\s*([\d,]+\.\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountCurrencyFirst();

    // Matches: "123.45 PLN", "+1,234.56 EUR"
    [GeneratedRegex(@"([+-]?)\s*([\d,]+\.\d{2})\s*(?:PLN|EUR|USD|GBP|CHF)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountCurrencyLast();

    // Formy osobowe („wydał(a)", „otrzymał(a)") sa tu obok bezosobowych, bo na koncie
    // wspólnym Revolut podaje sprawcę: „Sylwester Rzepka wydał(a) 11,13 zł." zamiast
    // „Wydano 11,13 zł.". Sam rdzeń wystarcza — obejmuje i „wydał", i „wydała",
    // i „wydał(a)". Rdzenia „płacił" celowo nie skracamy: pasowałby jednocześnie do
    // „zapłacił" (wydatek) i „wpłacił" (wpływ), więc oba stoją w pełnym brzmieniu.
    private static readonly string[] IncomeWordsPl =
        ["otrzymano", "otrzymał", "wpłynęło", "zwrot", "doładowano", "cashback", "wpłata", "wpłacił"];
    private static readonly string[] ExpenseWordsPl =
        ["wydano", "wydał", "zapłacono", "zapłacił", "płatność", "obciążenie", "pobrano"];

    private static readonly string[] IncomeWordsEn =
        ["received", "refund", "cashback", "topped up", "added", "money in"];
    private static readonly string[] ExpenseWordsEn =
        ["paid", "payment", "spent", "sent", "withdrawn", "charge", "declined", "money out"];

    public ParsedTransaction? TryParse(string title, string text)
    {
        title = NotificationText.Normalize(title);
        text  = NotificationText.Normalize(text);

        var combined = $"{title} {text}";
        var lower = combined.ToLowerInvariant();

        var polish = TryParsePolish(title, combined, lower);
        if (polish is not null) return polish;

        return TryParseInternational(text, combined, lower);
    }

    private static ParsedTransaction? TryParsePolish(string title, string combined, string lower)
    {
        var m = PolishAmount().Match(combined);
        if (!m.Success) return null;

        var intPart = m.Groups[1].Value.Replace(" ", "");
        var decPart = m.Groups[2].Success ? m.Groups[2].Value : "00";
        if (!decimal.TryParse($"{intPart}.{decPart}", NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

        int sign;
        if (IncomeWordsPl.Any(lower.Contains))
            sign = 1;
        else if (ExpenseWordsPl.Any(lower.Contains))
            sign = -1;
        else
            return null;

        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero) * sign;

        // Tytuł "Konto wspólne · Lidl" → sprzedawcą jest segment po ostatnim " · ".
        var merchant = title;
        var sepIdx = title.LastIndexOf(" · ", StringComparison.Ordinal);
        if (sepIdx >= 0) merchant = title[(sepIdx + 3)..].Trim();

        return new ParsedTransaction(new Money(grosze), merchant);
    }

    private static ParsedTransaction? TryParseInternational(string text, string combined, string lower)
    {
        var m = AmountCurrencyFirst().Match(combined);
        if (!m.Success) m = AmountCurrencyLast().Match(combined);
        if (!m.Success) return null;

        var signStr = m.Groups[1].Value;
        var amountStr = m.Groups[2].Value.Replace(",", "");
        if (!decimal.TryParse(amountStr, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

        int sign;
        if (signStr == "-")
            sign = -1;
        else if (signStr == "+")
            sign = 1;
        else if (IncomeWordsEn.Any(lower.Contains))
            sign = 1;
        else if (ExpenseWordsEn.Any(lower.Contains))
            sign = -1;
        else
            return null;

        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero) * sign;
        var merchant = text.Length > 80 ? text[..80] : text;
        return new ParsedTransaction(new Money(grosze), merchant.Trim());
    }
}
