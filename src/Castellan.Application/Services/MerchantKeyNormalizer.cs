using System.Text.RegularExpressions;

namespace Castellan.Application.Services;

public static partial class MerchantKeyNormalizer
{
    private static readonly string[] AggregatorPrefixes =
    [
        "GOOGLE PAY ", "APPLE PAY ", "PAYPAL*", "PAYU*",
        "PRZELEWY24 ", "TPAY ", "PAYPAL ", "PAYU ", "BLIK ",
    ];

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.ToUpperInvariant();

        // Replace non-alphanumeric (except Polish letters) with space
        s = NonAlphaRegex().Replace(s, " ");
        s = CollapseSpacesRegex().Replace(s, " ").Trim();

        // Strip known aggregator prefixes (longest match wins — array is ordered longest-first)
        foreach (var prefix in AggregatorPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..].TrimStart();
                break;
            }
        }

        // Remove trailing point/location tokens: Z1234, NR 12, #345, standalone numbers
        s = TrailingTokenRegex().Replace(s, "").TrimEnd();

        if (s.Length == 0) return null;
        return s.Length > 40 ? s[..40] : s;
    }

    [GeneratedRegex(@"[^A-ZĄĆĘŁŃÓŚŹŻ0-9 ]")]
    private static partial Regex NonAlphaRegex();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex CollapseSpacesRegex();

    [GeneratedRegex(@"\s+(?:NR\s+\d+|[A-Z]?\d{2,}|#\d+)\s*$")]
    private static partial Regex TrailingTokenRegex();
}
