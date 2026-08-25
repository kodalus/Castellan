using Castellan.Domain;

namespace Castellan.App.ViewModels;

/// <summary>
/// Lista do Pickera z bankiem. Pierwsza pozycja to „nie ustawiony" — bez niej wybór
/// byłby przymusowy, a konto w banku, którego powiadomień aplikacja nie czyta, musiałoby
/// dostać cudzą etykietę.
/// </summary>
public static class BankChoices
{
    public const string None = "(nie ustawiony)";

    public static List<string> Options { get; } = [None, .. Banks.Known];

    public static int IndexOf(string? bankKey) =>
        bankKey is null ? 0 : Math.Max(0, Options.IndexOf(bankKey));

    public static string? FromIndex(int index) =>
        index <= 0 || index >= Options.Count ? null : Options[index];
}
