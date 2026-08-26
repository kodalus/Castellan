using System.Globalization;
using System.Text.RegularExpressions;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record UnrecognizedNotification(
    RawNotificationId Id,
    string SourceName,
    DateTimeOffset PostedAt,
    string Title,
    string Text,
    Money Amount);

/// <summary>
/// Powiadomienia zapisane, ale nierozczytane. Do wczoraj przepadały bez śladu: żaden
/// ekran nie odwoływał się do ParseStatus, więc format, którego parser nie znał, znikał
/// po cichu — tak przez wiele tygodni gubiły się przelewy własne ING. Banki zmieniają
/// brzmienie komunikatów, więc to nie jest sytuacja wyjątkowa, tylko powracająca.
///
/// Lista jest ZAWĘŻONA DO TYCH, KTÓRE ZAWIERAJĄ KWOTĘ. ING i Revolut przysyłają też
/// reklamy i przypomnienia o logowaniu; gdyby wchodziły tutaj, lista zalałaby się
/// szumem i przestano by na nią patrzeć w tydzień. Jeśli w treści są pieniądze, a mimo
/// to nic z niej nie powstało — to jest dokładnie ten przypadek, o którym trzeba wiedzieć.
/// </summary>
public sealed partial class GetUnrecognizedNotificationsUseCase(IRawNotificationRepository rawNotifications)
{
    [GeneratedRegex(@"(\d[\d ]*)(?:,(\d{2}))?\s*(?:PLN|zł)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    private static readonly Dictionary<string, string> SourceNames = new(StringComparer.Ordinal)
    {
        ["pl.ing.mojeing"] = "ING",
        ["com.revolut.revolut"] = "Revolut",
        ["com.google.android.apps.walletnfcrel"] = "Portfel Google",
    };

    public async Task<IReadOnlyList<UnrecognizedNotification>> ExecuteAsync(CancellationToken ct = default)
    {
        var unparsed = await rawNotifications.ListUnparsedAsync(ct: ct);

        var rows = new List<UnrecognizedNotification>();
        foreach (var n in unparsed)
        {
            if (TryReadAmount($"{n.Title} {n.Text}") is not { } amount) continue;

            rows.Add(new UnrecognizedNotification(
                n.Id,
                SourceNames.GetValueOrDefault(n.PackageName, n.PackageName),
                n.PostedAt,
                n.Title,
                n.Text,
                amount));
        }

        return rows;
    }

    /// <summary>
    /// Sama kwota, bez rozstrzygania znaku — kierunku w nierozpoznanej treści z definicji
    /// nie znamy. Wpisując transakcję ręcznie, użytkownik i tak wybiera wydatek albo wpływ.
    /// </summary>
    private static Money? TryReadAmount(string text)
    {
        var m = AmountPattern().Match(text);
        if (!m.Success) return null;

        var intPart = m.Groups[1].Value.Replace(" ", "");
        var decPart = m.Groups[2].Success ? m.Groups[2].Value : "00";
        if (!decimal.TryParse($"{intPart}.{decPart}", NumberStyles.Number,
                CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return null;

        return new Money((long)Math.Round(dec * 100, MidpointRounding.AwayFromZero));
    }
}
