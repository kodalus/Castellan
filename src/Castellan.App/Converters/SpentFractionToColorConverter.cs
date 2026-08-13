using System.Globalization;

namespace Castellan.App.Converters;

public sealed class SpentFractionToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d ? d : 0.0;
        return fraction > 1.0 ? Color.FromArgb("#F44336")   // red — over budget
             : fraction >= 0.75 ? Color.FromArgb("#FF9800") // orange — approaching limit
             : Color.FromArgb("#4CAF50");                   // green — healthy
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
