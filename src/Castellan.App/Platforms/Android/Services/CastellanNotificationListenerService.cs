using Android.App;
using Android.Content;
using Android.Service.Notification;
using Castellan.App.Services;
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

        var packageName = sbn.PackageName ?? "";

        // First filter: not in whitelist → discard immediately, no logging
        if (!IngestRawNotificationUseCase.AllowedPackages.Contains(packageName)) return;

        var extras = sbn.Notification?.Extras;
        var title = extras?.GetString(global::Android.App.Notification.ExtraTitle) ?? "";
        var text  = extras?.GetString(global::Android.App.Notification.ExtraText)  ?? "";

        var postedAt = DateTimeOffset.FromUnixTimeMilliseconds(sbn.PostTime);

        _ = Task.Run(async () =>
        {
            // Każdy etap osobno, bo awaria każdego z nich wygląda tak samo: powiadomienie
            // przychodzi i nic się nie dzieje. Bez rozróżnienia zostaje zgadywanie, czy
            // zawiodło uprawnienie, kontekst MAUI, kontener, czy zapis do bazy.
            var stage = "kontekst MAUI";
            try
            {
                var services = IPlatformApplication.Current?.Services;
                if (services is null)
                {
                    CaptureDiagnostics.RecordError(stage,
                        new InvalidOperationException("IPlatformApplication.Current.Services jest null"));
                    return;
                }

                // Znacznik „nasłuch żyje" stawiamy niezależnie od trybu: dowodzi, że
                // serwis nie został ubity, więc po powrocie do trybu powiadomień
                // Główna nie wyświetli od razu ostrzeżenia o dobowej ciszy.
                stage = "znacznik czasu";
                Microsoft.Maui.Storage.Preferences.Set(
                    "last_notification_at", DateTimeOffset.UtcNow.Ticks);

                // Tryb ręczny: użytkownik wpisuje wszystko sam, więc założenie
                // transakcji z powiadomienia zdublowałoby jego wpis. Uprawnienie
                // bywa przyznane z wcześniejszego użycia, więc nie wystarczy liczyć
                // na to, że system nas nie zawoła.
                //
                // Sprawdzenie MUSI być tutaj, wewnątrz try i za kontrolą kontekstu
                // MAUI. Preferences sięga po MAUI Essentials, a Android wskrzesza
                // ten serwis bez aktywności — wywołane wyżej, poza siecią
                // bezpieczeństwa, potrafi rzucić wyjątkiem, który wychodzi
                // z OnNotificationPosted i cicho wyłącza nasłuch.
                stage = "tryb pracy";
                if (!AppSettings.UsesNotifications) return;

                stage = "kontener zależności";
                using var scope = services.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<IngestRawNotificationUseCase>();

                stage = "zapis do bazy";
                await useCase.ExecuteAsync(new IngestRawNotificationUseCase.Input(packageName, title, text, postedAt));
            }
            catch (Exception ex)
            {
                // Must never propagate — an unhandled exception disables the service silently.
                // Log Androida zostaje dla podpiętego telefonu; plik jest po to, żeby dało
                // się zobaczyć przyczynę w samej aplikacji, bez kabla i bez logcata.
                CaptureDiagnostics.RecordError(stage, ex);
                global::Android.Util.Log.Error("Castellan.NLS", ex.ToString());
            }
        });
    }
}
