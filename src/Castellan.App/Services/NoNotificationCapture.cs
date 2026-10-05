namespace Castellan.App.Services;

/// <summary>
/// Przechwytywanie powiadomień bankowych istnieje tylko na Androidzie — to funkcja
/// systemu, nie aplikacji. Na pulpicie nie ma czego pytać o uprawnienie, więc ta
/// implementacja mówi wprost: nie ma zgody i nie ma gdzie jej szukać.
///
/// Istnieje, bo <c>InboxViewModel</c> wymaga tej usługi w konstruktorze. Bez niej
/// kontener wywaliłby się przy pierwszej próbie utworzenia tego widoku — a to się
/// zdarzy niezależnie od ukrycia zakładki, choćby przez nawigację po trasie.
/// </summary>
public sealed class NoNotificationCapture : INotificationPermissionService
{
    public bool IsGranted() => false;

    public void OpenSettings() { }
}
