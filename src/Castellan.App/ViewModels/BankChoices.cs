using Castellan.Domain;

namespace Castellan.App.ViewModels;

/// <summary>
/// Lista do Pickera z bankiem. Dwie pozycje nie są bankami:
///
/// „(nie ustawiony)" — bez niej wybór byłby przymusowy, a konto w banku, którego
/// powiadomień aplikacja nie czyta, musiałoby dostać cudzą etykietę.
///
/// „Inny (wpisz)" — polskich banków i kas jest kilkadziesiąt razem ze spółdzielczymi,
/// więc każda zamknięta lista będzie dla kogoś niepełna. Wolna wartość jest tu
/// odpowiedzią ostateczną, a lista tylko skrótem do tych najczęstszych.
/// </summary>
public static class BankChoices
{
    public const string None = "(nie ustawiony)";
    public const string Custom = "Inny (wpisz)";

    public static List<string> Options { get; } = [None, .. Banks.Popular, Custom];

    public static int CustomIndex => Options.Count - 1;

    /// <summary>
    /// Bank spoza listy wskazuje na „Inny (wpisz)", a nie na „(nie ustawiony)".
    /// Inaczej otwarcie edycji konta z własnym bankiem pokazywałoby puste pole,
    /// a zapis po cichu zjadałby wpisaną nazwę.
    /// </summary>
    public static int IndexOf(string? bankKey)
    {
        if (string.IsNullOrWhiteSpace(bankKey)) return 0;

        var known = Options.IndexOf(bankKey);
        return known > 0 ? known : CustomIndex;
    }

    /// <summary>Nazwa wpisana ręcznie, gdy wybrano „Inny (wpisz)" — inaczej null.</summary>
    public static string? CustomTextFor(string? bankKey) =>
        IndexOf(bankKey) == CustomIndex ? bankKey : null;

    public static string? FromIndex(int index, string? customText)
    {
        if (index == CustomIndex)
            return string.IsNullOrWhiteSpace(customText) ? null : customText.Trim();

        return index <= 0 || index >= Options.Count ? null : Options[index];
    }
}
