using System.Text;

namespace Castellan.App.Services;

/// <summary>
/// Ostatni błąd przechwytywania powiadomienia, zapisany tak, żeby dało się go zobaczyć
/// W APLIKACJI, a nie tylko w logu Androida.
///
/// Powód jest praktyczny: nasłuch łapie każdy wyjątek i tylko go loguje — musi, bo
/// wyjątek wypuszczony z <c>OnNotificationPosted</c> po cichu wyłącza usługę. Skutkiem
/// ubocznym było jednak to, że awaria wygląda dokładnie jak cisza: powiadomienia
/// przychodzą, nic się nie zapisuje i nie ma śladu dlaczego. Bez telefonu w ręku nie da
/// się wtedy odróżnić zerwanego uprawnienia od błędu w kodzie.
///
/// ŚWIADOMIE bez bazy danych i bez MAUI Essentials. Jedno i drugie może być właśnie tym,
/// co nie działa — a narzędzie diagnostyczne, które przewraca się z tego samego powodu co
/// diagnozowana usterka, jest bezużyteczne. Zostaje zwykły plik i ścieżka z BCL.
/// </summary>
public static class CaptureDiagnostics
{
    private const int MaxEntries = 20;

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "capture-errors.log");

    /// <summary>
    /// Nigdy nie rzuca. Wołane z bloku catch nasłuchu — gdyby samo mogło się wywrócić,
    /// zabrałoby ze sobą jedyną sieć bezpieczeństwa, jaką ma usługa.
    /// </summary>
    public static void RecordError(string stage, Exception ex)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{stage}] {Describe(ex)}";
            var kept = Read().Append(line).TakeLast(MaxEntries);
            File.WriteAllLines(LogPath, kept, Encoding.UTF8);
        }
        catch
        {
            // Diagnostyka nie ma prawa zaszkodzić temu, co diagnozuje.
        }
    }

    public static IReadOnlyList<string> Read()
    {
        try
        {
            return File.Exists(LogPath) ? File.ReadAllLines(LogPath, Encoding.UTF8) : [];
        }
        catch
        {
            return [];
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(LogPath)) File.Delete(LogPath);
        }
        catch
        {
            // jw.
        }
    }

    /// <summary>Cały łańcuch wyjątków — przyczyna bywa dopiero w drugim albo trzecim.</summary>
    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
            sb.Append($"[{e.GetType().Name}] {e.Message} ");
        return sb.ToString().Trim();
    }
}
