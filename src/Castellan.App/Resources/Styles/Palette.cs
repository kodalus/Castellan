namespace Castellan.App.Resources.Styles;

/// <summary>
/// Dostęp do kolorów z Colors.xaml z poziomu kodu. Wykresy i konwertery rysują się
/// poza XAML-em, więc bez tego musiałyby powtarzać wartości hex u siebie — a wtedy
/// zmiana palety w jednym miejscu zostawiłaby słupki w starych barwach.
/// </summary>
internal static class Palette
{
    public static Color Get(string key) =>
        Microsoft.Maui.Controls.Application.Current?.Resources
            .TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Gray;

    public static Color Negative    => Get("Negative");
    public static Color NegativeDim => Get("NegativeDim");
    public static Color Positive    => Get("Positive");
    public static Color PositiveDim => Get("PositiveDim");
    public static Color Brass       => Get("Brass");
}
