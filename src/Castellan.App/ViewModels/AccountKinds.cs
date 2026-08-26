using Castellan.Domain;

namespace Castellan.App.ViewModels;

/// <summary>
/// Wspólna lista typów konta dla obu formularzy. Wcześniej każdy trzymał własną tablicę
/// i własne „indeks 1 znaczy oszczędnościowe" — przy trzeciej pozycji taki zapis milczkiem
/// przestaje działać w jednym miejscu, a w drugim nie.
/// </summary>
public static class AccountKinds
{
    private static readonly (AccountKind Kind, string Label)[] All =
    [
        (AccountKind.Checking, "Rachunek bieżący"),
        (AccountKind.Savings,  "Oszczędnościowe"),
        (AccountKind.Cash,     "Gotówka"),
    ];

    public static List<string> Options { get; } = [.. All.Select(x => x.Label)];

    public static int IndexOf(AccountKind kind) =>
        Math.Max(0, Array.FindIndex(All, x => x.Kind == kind));

    public static AccountKind FromIndex(int index) =>
        index >= 0 && index < All.Length ? All[index].Kind : AccountKind.Checking;
}
