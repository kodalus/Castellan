using Android.Content;
using AndroidX.Core.App;
using Castellan.App.Services;

namespace Castellan.App.Platforms.Android.Services;

public class AndroidNotificationPermissionService : INotificationPermissionService
{
    public bool IsGranted()
    {
        var context = global::Android.App.Application.Context;
        var enabled = NotificationManagerCompat.GetEnabledListenerPackages(context);
        return context.PackageName is { } pkg && (enabled?.Contains(pkg) == true);
    }

    public void OpenSettings()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(global::Android.Provider.Settings.ActionNotificationListenerSettings);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
