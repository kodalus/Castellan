using System.Windows.Input;

namespace Castellan.App.Services;

/// <summary>
/// Spina pola formularza w jeden ciąg klawiaturowy: Enter przechodzi do następnego pola,
/// a w ostatnim zapisuje. Do tego kursor ląduje w pierwszym polu zaraz po otwarciu ekranu.
///
/// Na telefonie to wygoda. Na pulpicie to jedyny znośny sposób wprowadzania danych:
/// przechwytywania powiadomień tam NIE MA, więc każda transakcja jest wpisywana ręcznie,
/// a sięganie myszą po każde kolejne pole przy kilkunastu wpisach dziennie jest karą.
/// </summary>
public static class KeyboardFlow
{
    /// <summary>
    /// Enter w polu przenosi do następnego, a w ostatnim uruchamia polecenie zapisu.
    /// Wołane raz, przy budowaniu strony — nie przy każdym wejściu, bo zdarzenia
    /// dopisywałyby się wtedy w kółko i jeden Enter zapisywałby wielokrotnie.
    /// </summary>
    public static void Chain(IReadOnlyList<Entry> fields, ICommand? save = null)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var next = i + 1 < fields.Count ? fields[i + 1] : null;

            fields[i].ReturnType = next is not null ? ReturnType.Next : ReturnType.Done;
            fields[i].Completed += (_, _) =>
            {
                if (next is not null) next.Focus();
                else if (save?.CanExecute(null) == true) save.Execute(null);
            };
        }
    }

    /// <summary>
    /// Kursor w polu od razu po otwarciu ekranu.
    ///
    /// <c>Focus()</c> wywołane przed ułożeniem widoku nie ma na czym stanąć i cicho nic
    /// nie robi, więc czekamy na <c>Loaded</c>, a potem jeszcze oddajemy sterowanie
    /// dispatcherowi — na WinUI ustawienie fokusu w samym Loaded bywa zjadane przez
    /// pierwsze przeliczenie układu.
    /// </summary>
    public static void FocusWhenReady(this VisualElement element)
    {
        if (element.IsLoaded)
        {
            Dispatch(element);
            return;
        }

        element.Loaded += OnLoaded;

        void OnLoaded(object? sender, EventArgs e)
        {
            element.Loaded -= OnLoaded;
            Dispatch(element);
        }

        static void Dispatch(VisualElement target) =>
            target.Dispatcher.Dispatch(() => target.Focus());
    }
}
