namespace Castellan.App.Services;

public interface INotificationPermissionService
{
    bool IsGranted();
    void OpenSettings();

    /// <summary>
    /// Prosi system o ponowne podpięcie nasłuchu powiadomień. Wołane wtedy, gdy widać,
    /// że powiadomienia przestały przychodzić — rozpięty nasłuch wygląda dokładnie tak
    /// samo jak przyznane uprawnienie, więc nie ma czego użytkownikowi pokazać poza ciszą.
    /// </summary>
    void RequestRebind();
}
