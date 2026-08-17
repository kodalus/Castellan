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

    /// <summary>
    /// Domyślnie tryb powiadomień: to jest sens istnienia aplikacji, a dla osób,
    /// które już jej używają, zmiana domyślnej wartości oznaczałaby ciche wyłączenie
    /// przechwytywania po aktualizacji.
    /// </summary>
    public static CaptureMode CaptureMode
    {
        get => Preferences.Get(CaptureModeKey, "") == ManualValue
            ? CaptureMode.Manual
            : CaptureMode.Notifications;
        set => Preferences.Set(CaptureModeKey, value == CaptureMode.Manual ? ManualValue : "notifications");
    }

    public static bool UsesNotifications => CaptureMode == CaptureMode.Notifications;
}
