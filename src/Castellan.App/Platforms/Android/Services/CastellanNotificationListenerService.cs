using Android.App;
using Android.Content;
using Android.Service.Notification;
using Castellan.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Castellan.App.Platforms.Android.Services;

[Service(
    Label = "Castellan — powiadomienia bankowe",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public class CastellanNotificationListenerService : NotificationListenerService
{
    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        if (sbn is null) return;

        // Tryb ręczny: użytkownik wpisuje wszystko sam, więc założenie transakcji
        // z powiadomienia zdublowałoby jego własny wpis. Odcinamy tu, na krawędzi
        // platformy — use case nie ma prawa wiedzieć o ustawieniach telefonu.
        // Uprawnienie może zostać przyznane z wcześniejszego użycia, więc nie
        // wystarczy polegać na tym, że system nas nie zawoła.
        if (!Castellan.App.Services.AppSettings.UsesNotifications) return;

        var packageName = sbn.PackageName ?? "";

        // First filter: not in whitelist → discard immediately, no logging
        if (!IngestRawNotificationUseCase.AllowedPackages.Contains(packageName)) return;

        var extras = sbn.Notification?.Extras;
        var title = extras?.GetString(global::Android.App.Notification.ExtraTitle) ?? "";
        var text  = extras?.GetString(global::Android.App.Notification.ExtraText)  ?? "";

        var postedAt = DateTimeOffset.FromUnixTimeMilliseconds(sbn.PostTime);

        _ = Task.Run(async () =>
        {
            try
            {
                var services = IPlatformApplication.Current?.Services;
                if (services is null) return;

                Microsoft.Maui.Storage.Preferences.Set(
                    "last_notification_at", DateTimeOffset.UtcNow.Ticks);

                using var scope = services.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<IngestRawNotificationUseCase>();
                await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(packageName, title, text, postedAt));
            }
            catch (Exception ex)
            {
                // Must never propagate — an unhandled exception disables the service silently
                global::Android.Util.Log.Error("Castellan.NLS", ex.ToString());
            }
        });
    }
}
