using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using AGColor = Android.Graphics.Color;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Castellan.App.Platforms.Android.Services;

[BroadcastReceiver(
    Name = "dev.castellan.app.CastellanWidgetProvider",
    Label = "Widget Castellan",
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

        try
        {
            var dbPath = System.IO.Path.Combine(
                context.FilesDir!.AbsolutePath, "castellan.db");

            if (!System.IO.File.Exists(dbPath))
            {
                SetPlaceholder(views, "Otwórz aplikację, aby zobaczyć dane");
            }
            else
            {
                var opts = new DbContextOptionsBuilder<CastellanDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options;

                using var db = new CastellanDbContext(opts);

                var now = DateTimeOffset.Now;
                var local = now.ToLocalTime();

                // Load then filter client-side — avoids DateTimeOffset column issues in SQLite
                var allTx = db.Transactions.ToList();
                var monthTx = allTx.Where(t =>
                    t.OccurredAt.ToLocalTime().Year  == local.Year &&
                    t.OccurredAt.ToLocalTime().Month == local.Month &&
                    !t.IsExcludedFromCalculations).ToList();

                var expenseGrosze = Math.Abs(monthTx
                    .Where(t => t.Amount.IsNegative)
                    .Sum(t => t.Amount.Grosze));
                var incomeGrosze = monthTx
                    .Where(t => !t.Amount.IsNegative)
                    .Sum(t => t.Amount.Grosze);
                var netGrosze = incomeGrosze - expenseGrosze;

                var expense = new Money(expenseGrosze);
                var net     = new Money(netGrosze);

                var monthName = new DateTime(local.Year, local.Month, 1)
                    .ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));

                views.SetTextViewText(Resource.Id.widget_month, $"Castellan · {monthName}");
                views.SetTextViewText(Resource.Id.widget_expense, expense.ToString());
                views.SetTextViewText(Resource.Id.widget_net, net.ToString());

                // Net color: green if positive/zero, red if negative
                var netColor = netGrosze >= 0
                    ? AGColor.Rgb(165, 214, 167)   // #A5D6A7
                    : AGColor.Rgb(255, 171, 145);  // #FFAB91
                views.SetTextColor(Resource.Id.widget_net, netColor);

                // Progress bar: expense / income * 100 (capped at 100)
                int progress = incomeGrosze == 0 ? 0
                    : (int)Math.Min(100L, expenseGrosze * 100L / incomeGrosze);
                views.SetProgressBar(Resource.Id.widget_progress, 100, progress, false);

                var label = incomeGrosze == 0
                    ? "Brak przychodów w tym miesiącu"
                    : $"{progress}% przychodów wydano";
                views.SetTextViewText(Resource.Id.widget_progress_label, label);
            }
        }
        catch (Exception ex)
        {
            SetPlaceholder(views, "Błąd odczytu danych");
            global::Android.Util.Log.Error("CastellanWidget", ex.ToString());
        }

        // Tap on widget → open app
        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        var pendingIntent = PendingIntent.GetActivity(
            context, 0, launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        views.SetOnClickPendingIntent(Resource.Id.widget_root, pendingIntent);

        manager.UpdateAppWidget(widgetId, views);
    }

    private static void SetPlaceholder(RemoteViews views, string message)
    {
        views.SetTextViewText(Resource.Id.widget_month, "Castellan");
        views.SetTextViewText(Resource.Id.widget_expense, "—");
        views.SetTextViewText(Resource.Id.widget_net, "—");
        views.SetTextViewText(Resource.Id.widget_progress_label, message);
        views.SetProgressBar(Resource.Id.widget_progress, 100, 0, false);
    }
}
