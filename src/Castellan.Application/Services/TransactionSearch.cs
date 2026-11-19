using System.Globalization;
using System.Text;

namespace Castellan.Application.Services;

/// <summary>
/// Dopasowanie tekstu wpisanego w lupkę do wiersza transakcji.
///
/// Trzy decyzje, wszystkie wzięte z tego, jak się naprawdę szuka na telefonie:
///
/// 1. Wszystkie słowa muszą pasować, ale kolejność nie ma znaczenia — „lidl 56"
///    znajdzie zakup w Lidlu za 56 zł, choć w wierszu te dwa kawałki stoją daleko
///    od siebie i w odwrotnej kolejności.
/// 2. Ogonki nie mają znaczenia w żadną stronę. „ortopede" znajdzie „ortopedę",
///    bo nikt nie przełącza klawiatury w środku szukania.
/// 3. Kropka i przecinek to to samo. Kwotę widać jako „56,81", ale wpisuje się
///    zwykle „56.81" — i odwrotnie przy dacie „19.11".
/// </summary>
public static class TransactionSearch
{
    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var haystack = Normalize(string.Join(" ", fields.Where(f => !string.IsNullOrWhiteSpace(f))));

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!haystack.Contains(Normalize(token), StringComparison.Ordinal))
                return false;

        return true;
    }

    private static string Normalize(string text)
    {
        var lowered = text.ToLowerInvariant().Replace('.', ',');

        // „ł" jako jedyna polska litera nie rozkłada się na literę i znak diakrytyczny,
        // więc normalizacja Unicode jej nie ruszy — trzeba ją podmienić osobno.
        var decomposed = lowered.Replace('ł', 'l').Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);

        return sb.ToString();
    }
}
