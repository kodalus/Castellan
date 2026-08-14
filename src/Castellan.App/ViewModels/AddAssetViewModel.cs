using System.Globalization;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record AssetLiquidityItem(AssetLiquidity Liquidity, string Display);

public partial class AddAssetViewModel : ObservableObject
{
    private readonly CreateAssetUseCase _create;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private AssetLiquidityItem? _selectedLiquidity;
    [ObservableProperty] private string _valueText = "";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public List<AssetLiquidityItem> Liquidities { get; } =
    [
        new(AssetLiquidity.Immediate, "Natychmiastowa (gotówka, ROR)"),
        new(AssetLiquidity.Fast,      "Szybka — konto oszczędnościowe"),
        new(AssetLiquidity.Medium,    "Średnia — obligacje, ETF"),
        new(AssetLiquidity.Slow,      "Wolna — nieruchomości, depozyt"),
    ];

    public AddAssetViewModel(CreateAssetUseCase create)
    {
        _create = create;
        SelectedLiquidity = Liquidities[0];
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name))    { ErrorMessage = "Podaj nazwę aktywa."; return; }
        if (SelectedLiquidity is null)           { ErrorMessage = "Wybierz poziom płynności."; return; }

        var grosze = ParseGrosze(ValueText);
        if (grosze < 0) { ErrorMessage = "Podaj poprawną wartość (może być 0)."; return; }

        IsBusy = true;
        try
        {
            await _create.ExecuteAsync(
                new CreateAssetCommand(Name.Trim(), SelectedLiquidity.Liquidity, new Money(grosze)),
                ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static long ParseGrosze(string text)
    {
        var normalized = text.Trim().Replace(',', '.').Replace(" ", "");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0)
            return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
        return -1;
    }
}
