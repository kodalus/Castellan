namespace Castellan.Infrastructure.Parsers;

/// <summary>
/// Ujednolica odstępy w treści powiadomienia przed dopasowaniem wzorców.
///
/// Polskie formatowanie kwot rozdziela tysiące spacją, ale banki wstawiają tam
/// spację TWARDĄ (U+00A0) — po to, żeby „1 600,00 zł" nigdy nie złamało się
/// w połowie liczby. Klasa znaków [\d ] we wzorcach obejmuje wyłącznie zwykłą
/// spację, więc „1<NBSP>600,00" było dopasowywane od drugiego członu i kwota
/// czytała się jako 600 zamiast 1600 — bez żadnego błędu, po prostu cicho o rząd
/// wielkości mniej.
///
/// Normalizacja jest po stronie parserów, a nie zapisu: w tabeli RawNotifications
/// treść ma zostać taka, jaką przysłał bank, bo to materiał do poprawiania wzorców.
/// </summary>
internal static class NotificationText
{
    private static readonly char[] SpaceLike =
    [
        '\u00A0', // spacja twarda — ta realnie przychodzi z ING
        '\u202F', // waska spacja twarda
        '\u2009', // spacja cienka
        '\u2007', // spacja o szerokosci cyfry
    ];

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var normalized = text;
        foreach (var c in SpaceLike)
            if (normalized.Contains(c))
                normalized = normalized.Replace(c, ' ');

        return normalized;
    }
}
