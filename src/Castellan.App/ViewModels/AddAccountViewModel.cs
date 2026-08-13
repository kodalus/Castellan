using System.Globalization;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class AddAccountViewModel : ObservableObject
{
    private readonly CreateAccountUseCase _create;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _kindIndex;
    [ObservableProperty] private string _balanceText = "0";

    public List<string> KindOptions { get; } = ["Rachunek bieżący", "Oszczędnościowe"];

    public AddAccountViewModel(CreateAccountUseCase create) => _create = create;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        if (!decimal.TryParse(BalanceText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)) return;

        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero);
        var kind = KindIndex == 1 ? AccountKind.Savings : AccountKind.Checking;

        try
        {
            await _create.ExecuteAsync(
                new CreateAccountUseCase.Input(Name.Trim(), kind, new Money(grosze), DateTimeOffset.Now), ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var e = ex;
            while (e != null)
            {
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
                e = e.InnerException;
            }
            var msg = sb.ToString();
            System.Diagnostics.Debug.WriteLine("[SaveAccount] " + msg);
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlert("Błąd zapisu", msg, "OK");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
