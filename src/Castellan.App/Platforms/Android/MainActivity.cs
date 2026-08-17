using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Castellan.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public const string RouteExtraKey = "castellan_route";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Musi się wykonać PRZED base.OnCreate — to ono uruchamia MAUI i konstruuje
        // AppShell, który przy starcie odczytuje PendingNavigation.
        HandleIntent(Intent);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        // Aktywność już istnieje (LaunchMode=SingleTop) — AppShell już działa,
        // więc PendingNavigation.Navigate nawiguje od razu.
        HandleIntent(intent);
    }

    private static void HandleIntent(Intent? intent)
    {
        var route = intent?.GetStringExtra(RouteExtraKey);
        if (!string.IsNullOrEmpty(route))
            PendingNavigation.Navigate(route);
    }
}
