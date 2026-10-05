using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Services;

/// <summary>
/// Miesiąc pokazywany na ekranie, który sam przechodzi na nowy miesiąc kalendarzowy.
///
/// Zakładki Shella są CACHE'OWANE: strona i jej ViewModel powstają raz i żyją tak długo
/// jak aplikacja, a telefon trzyma ją w pamięci tygodniami. Miesiąc ustawiony raz
/// w konstruktorze zostawał więc na wrześniu po 1 października — Plan pokazywał koperty
/// miesiąca, który się skończył, a Transakcje listę, do której nic już nie dochodziło.
/// Jedynym sposobem wyjścia było przełączenie miesiąca ręcznie albo ubicie aplikacji.
///
/// Reguła: ekran chodzi za kalendarzem, DOPÓKI użytkownik sam nie przełączy się na inny
/// miesiąc. Powrót na miesiąc bieżący wznawia chodzenie — więc nie ma stanu, z którego
/// nie dałoby się wyjść bez restartu.
/// </summary>
public sealed class MonthCursor
{
    private readonly Func<YearMonth> _today;
    private bool _followsCalendar = true;

    public MonthCursor(Func<YearMonth>? today = null)
    {
        _today = today ?? (() => YearMonth.Current);
        Month = _today();
    }

    public YearMonth Month { get; private set; }

    /// <summary>Wołane przy każdym wejściu na ekran — tam, gdzie zmiana miesiąca ma się ujawnić.</summary>
    public YearMonth Refresh()
    {
        if (_followsCalendar) Month = _today();
        return Month;
    }

    public YearMonth Previous() => MoveTo(Month.Previous());

    public YearMonth Next() => MoveTo(Month.Next());

    private YearMonth MoveTo(YearMonth target)
    {
        Month = target;

        // Zatrzymanie się na miesiącu bieżącym to nie „przeglądanie historii", tylko
        // powrót do normy — stąd chodzenie za kalendarzem wraca samo.
        _followsCalendar = target == _today();
        return Month;
    }
}
