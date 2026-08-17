using Android.Views;
using Google.Android.Material.BottomNavigation;

// Globalne using-i MAUI wciągają Microsoft.Maui.Controls.View, więc samo „View”
// jest tu dwuznaczne — poniżej chodzi wyłącznie o widok Androida.
using AView = Android.Views.View;

namespace Castellan.App;

/// <summary>
/// Przy więcej niż pięciu zakładkach MAUI dokłada na pasku pozycję „więcej” i podpisuje
/// ją zaszytym w środku angielskim słowem „More” — jedynym angielskim napisem w całej
/// aplikacji. Nie da się go podmienić ani ustawieniami, ani zasobami, więc zdejmujemy go
/// po stronie Androida: sama ikona trzech kropek mówi wystarczająco dużo.
///
/// Tytuł zerujemy zamiast usuwać etykietę, bo pusty napis zostawia tę samą wysokość co
/// podpisy pozostałych zakładek — dzięki temu ikona trzech kropek stoi w jednej linii
/// z resztą, a nie wyżej od nich.
/// </summary>
internal sealed class MoreTabLabel : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
{
    private const string MauiMoreTitle = "More";

    private readonly AView _root;

    private MoreTabLabel(AView root) => _root = root;

    public static void StripFrom(AView? root)
    {
        if (root?.ViewTreeObserver is not { IsAlive: true } observer) return;
        observer.AddOnGlobalLayoutListener(new MoreTabLabel(root));
    }

    // Pasek zakładek powstaje długo po starcie aktywności i jest przebudowywany przy
    // każdej zmianie zakładki, więc jednorazowe podejście by nie wystarczyło. Po
    // wyczyszczeniu tytułu kolejne wywołania nic nie robią, więc nie ma pętli.
    public void OnGlobalLayout()
    {
        if (FindBottomNav(_root) is not { } nav) return;

        var menu = nav.Menu;
        for (var i = 0; i < menu.Size(); i++)
        {
            var item = menu.GetItem(i);
            if (item is not null && string.Equals(
                    item.TitleFormatted?.ToString(), MauiMoreTitle, StringComparison.Ordinal))
                item.SetTitle(string.Empty);
        }
    }

    private static BottomNavigationView? FindBottomNav(AView view)
    {
        if (view is BottomNavigationView nav) return nav;
        if (view is not ViewGroup group) return null;

        for (var i = 0; i < group.ChildCount; i++)
        {
            if (group.GetChildAt(i) is { } child && FindBottomNav(child) is { } found)
                return found;
        }
        return null;
    }
}
