using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace Castellan.App.Platforms.Android.Services;

[BroadcastReceiver(
    Name = "dev.castellan.app.CastellanWidgetProvider",
    Label = "Szybka transakcja Castellan",
    Exported = true)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_info")]
public class CastellanWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(
        Context? context,
        AppWidgetManager? appWidgetManager,
        int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null) return;

        foreach (var widgetId in appWidgetIds)
            UpdateWidget(context, appWidgetManager, widgetId);
    }

    private static void UpdateWidget(Context context, AppWidgetManager manager, int widgetId)
    {
        var views = new RemoteViews(context.PackageName!, Resource.Layout.widget_main);

        // Tap on widget → open app straight into quick-add
        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        launchIntent.PutExtra(MainActivity.RouteExtraKey, "quickAdd");
        var pendingIntent = PendingIntent.GetActivity(
            context, 0, launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        views.SetOnClickPendingIntent(Resource.Id.widget_root, pendingIntent);

        manager.UpdateAppWidget(widgetId, views);
    }
}
