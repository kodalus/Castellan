namespace Castellan.App;

/// <summary>
/// Widget na ekranie głównym otwiera aplikację zwykłym Intentem do MainActivity —
/// żeby "kliknięcie" mogło od razu wylądować na konkretnym ekranie (transakcja
/// błyskawiczna), MainActivity przekazuje tu docelową trasę, a Shell konsumuje ją
/// raz po starcie (zimny start) albo natychmiast, jeśli już działa (ciepły start).
/// </summary>
public static class PendingNavigation
{
    private static string? _pendingRoute;
    private static Shell? _shell;

    public static void Navigate(string route)
    {
        if (_shell is { } shell)
            shell.Dispatcher.Dispatch(async () => await shell.GoToAsync(route));
        else
            _pendingRoute = route;
    }

    public static void Attach(Shell shell)
    {
        _shell = shell;
        if (_pendingRoute is not { } route) return;

        _pendingRoute = null;
        shell.Dispatcher.Dispatch(async () => await shell.GoToAsync(route));
    }
}
