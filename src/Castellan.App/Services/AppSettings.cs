namespace Castellan.App.Services;

/// <summary>Skąd biorą się transakcje.</summary>
public enum CaptureMode
{
    /// <summary>Aplikacja czyta powiadomienia bankowe i sama zakłada transakcje.</summary>
    Notifications,

    /// <summary>Wszystko wpisuje użytkownik. Powiadomienia są ignorowane.</summary>
    Manual,
}

/// <summary>
/// Ustawienia, które nie są danymi budżetu, więc nie mają czego szukać w bazie ani
/// w kopii zapasowej — dotyczą tego konkretnego telefonu, a nie finansów.
/// </summary>
public static class AppSettings
{
    private const string CaptureModeKey = "capture_mode";
    private const string ManualValue = "manual";
    private const string NotificationsValue = "notifications";

    /// <summary>
    /// Na Androidzie domyślnie tryb powiadomień: to jest sens istnienia aplikacji,
    /// a dla osób, które już jej używają, zmiana domyślnej wartości oznaczałaby ciche
    /// wyłączenie przechwytywania po aktualizacji.
    ///
    /// Na pulpicie domyślnie tryb ręczny, bo przechwytywania tam NIE MA — to funkcja
    /// Androida. Zostawienie tamtej domyślnej wartości dałoby stałe ostrzeżenie
    /// „powiadomienia nie przyszły od ponad doby" u kogoś, kto nigdy ich nie dostanie.
    /// </summary>
    private static CaptureMode DefaultMode =>
#if ANDROID
        CaptureMode.Notifications;
#else
        CaptureMode.Manual;
#endif

    /// <summary>
    /// Zapisana wartość ma pierwszeństwo nad domyślną, ale TYLKO jeśli coś zapisano.
    /// Dawna wersja czytała „cokolwiek poza manual" jako tryb powiadomień, więc pusta
    /// preferencja na pulpicie wychodziłaby na tryb powiadomień wbrew domyślnej.
    /// </summary>
    public static CaptureMode CaptureMode
    {
        get => Preferences.Get(CaptureModeKey, "") switch
        {
            ManualValue        => CaptureMode.Manual,
            NotificationsValue => CaptureMode.Notifications,
            _                  => DefaultMode,
        };
        set => Preferences.Set(CaptureModeKey, value == CaptureMode.Manual ? ManualValue : NotificationsValue);
    }

    public static bool UsesNotifications => CaptureMode == CaptureMode.Notifications;
}
