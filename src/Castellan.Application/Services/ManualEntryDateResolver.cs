namespace Castellan.Application.Services;

public static class ManualEntryDateResolver
{
    /// <summary>
    /// Data z pickera nie niesie godziny (domyślnie północ). Saldo konta liczy tylko
    /// transakcje, których OccurredAt wypada PO momencie ostatniego uzgodnienia — jeśli
    /// konto założono dziś po południu, a transakcja z domyślną datą "dziś" dostałaby
    /// północ, wypadłaby PRZED uzgodnieniem i znikałaby z salda mimo poprawnego zapisu.
    /// "Dziś" dostaje więc aktualny czas; dni wsteczne — koniec dnia (bezpiecznie po
    /// ewentualnym uzgodnieniu z tego samego dnia).
    /// </summary>
    public static DateTimeOffset Resolve(DateTime pickedDate, DateTimeOffset now)
    {
        if (pickedDate.Date == now.Date) return now;
        return new DateTimeOffset(pickedDate.Year, pickedDate.Month, pickedDate.Day, 23, 59, 59, now.Offset);
    }
}
