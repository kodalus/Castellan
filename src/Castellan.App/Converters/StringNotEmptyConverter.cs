using System.Globalization;

namespace Castellan.App.Converters;

/// <summary>
/// Ukrywa etykietę, gdy tekst jest pusty. Bez tego pusty notatnik przy transakcji
/// nadal zajmowałby wiersz w liście — wszystkie pozycje byłyby wtedy wyższe, a odstępy
/// między nimi nierówne w zależności od tego, czy ktoś wpisał opis.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
