using System.Globalization;

namespace Castellan.Domain.ValueObjects;

public readonly record struct Money(long Grosze) : IComparable<Money>
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    public static Money Zero => new(0);

    public bool IsNegative => Grosze < 0;

    public Money Abs() => new(Math.Abs(Grosze));

    public static Money operator +(Money a, Money b) => new(a.Grosze + b.Grosze);
    public static Money operator -(Money a, Money b) => new(a.Grosze - b.Grosze);
    public static Money operator -(Money a) => new(-a.Grosze);
    public static bool operator <(Money a, Money b) => a.Grosze < b.Grosze;
    public static bool operator >(Money a, Money b) => a.Grosze > b.Grosze;
    public static bool operator <=(Money a, Money b) => a.Grosze <= b.Grosze;
    public static bool operator >=(Money a, Money b) => a.Grosze >= b.Grosze;

    public int CompareTo(Money other) => Grosze.CompareTo(other.Grosze);

    public override string ToString() =>
        (Grosze / 100m).ToString("#,##0.00 zł", Polish);
}

public static class MoneyExtensions
{
    public static Money Sum(this IEnumerable<Money> source)
        => new(source.Sum(m => m.Grosze));
}
