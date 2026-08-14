using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(AssetId), "assetId")]
public partial class UpdateAssetValueViewModel : ObservableObject
{
    private readonly IAssetRepository _assets;
    private readonly UpdateAssetValueUseCase _update;

    [ObservableProperty] private string _assetId = "";
    [ObservableProperty] private string _assetName = "—";
    [ObservableProperty] private string _currentValueDisplay = "—";
    [ObservableProperty] private string _newValueText = "";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private AssetId? _id;

    public UpdateAssetValueViewModel(IAssetRepository assets, UpdateAssetValueUseCase update)
    {
        _assets = assets;
        _update = update;
    }

    partial void OnAssetIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new AssetId(guid);
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (_id is not { } id) return;
        var asset = await _assets.GetAsync(id);
        if (asset is null) return;
        AssetName           = asset.Name;
        CurrentValueDisplay = $"{asset.Value.Grosze / 100m:N2} zł";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        var grosze = ParseGrosze(NewValueText);
        if (grosze < 0) { ErrorMessage = "Podaj poprawną kwotę (może być 0)."; return; }

        IsBusy = true;
        try
        {
            await _update.ExecuteAsync(id, new Money(grosze), ct);
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
