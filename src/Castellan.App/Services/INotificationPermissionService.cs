namespace Castellan.App.Services;

public interface INotificationPermissionService
{
    bool IsGranted();
    void OpenSettings();
}
