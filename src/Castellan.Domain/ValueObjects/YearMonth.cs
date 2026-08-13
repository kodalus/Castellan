namespace Castellan.Domain.ValueObjects;

public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth Current => From(DateTimeOffset.Now);

    public static YearMonth From(DateTimeOffset dt)
    {
        var local = dt.ToLocalTime();
        return new(local.Year, local.Month);
    }

    public bool Contains(DateTimeOffset dt)
    {
        var local = dt.ToLocalTime();
        return local.Year == Year && local.Month == Month;
    }

    public YearMonth Next() => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);
    public YearMonth Previous() => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);

    public int CompareTo(YearMonth other)
        => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    public static bool TryParse(string? s, out YearMonth result)
    {
        result = default;
        if (s is null) return false;
        var parts = s.Split('-');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)) return false;
        if (m < 1 || m > 12) return false;
        result = new YearMonth(y, m);
        return true;
    }

    public string ToDisplayString()
    {
        var dt = new DateTime(Year, Month, 1);
        return dt.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));
    }
}
