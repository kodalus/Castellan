using System.Globalization;
using Castellan.App.Resources.Styles;

namespace Castellan.App.Converters;

/// <summary>
/// Kolory bierze z palety aplikacji zamiast mieć własne — inaczej „przekroczone”
/// na pasku postępu świeciłoby innym odcieniem niż „przekroczone” w tekście obok.
/// </summary>
public sealed class SpentFractionToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d ? d : 0.0;
        return fraction > 1.0 ? Palette.Negative
             : fraction >= 0.75 ? Palette.Brass
             : Palette.Positive;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
