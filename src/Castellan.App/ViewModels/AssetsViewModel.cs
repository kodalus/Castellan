using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

// ── VM wrappers for XAML binding ──────────────────────────────────────────────

public sealed class AssetRowVm
{
    public AssetId Id            { get; }
    public string Name           { get; }
    public string ValueDisplay   { get; }
    public string UpdatedDisplay { get; }
    public ICommand UpdateCommand { get; }

    public bool IsAccount { get; }

    public AssetRowVm(AssetRow row, ICommand updateCommand)
    {
        Id             = row.Id;
        Name           = row.Name;
        IsAccount      = row.IsAccount;
        ValueDisplay   = $"{row.Value.Grosze / 100m:N2} zł";
        UpdatedDisplay = row.IsAccount ? "saldo konta" : row.UpdatedOn.ToString("d.MM.yyyy");
        UpdateCommand  = updateCommand;
    }
}

public sealed class CushionTierVm
{
    public string LiquidityDisplay  { get; }
    public string MonthsTierDisplay { get; }
    public string CumulativeDisplay { get; }
    public string TierValueDisplay  { get; }
    public bool   HasAssets         { get; }
    public IReadOnlyList<AssetRowVm> Assets { get; }

    public CushionTierVm(CushionTier tier, ICommand updateCommand)
    {
        LiquidityDisplay  = tier.LiquidityDisplay;
        MonthsTierDisplay = tier.MonthsTier > 0 ? $"{tier.MonthsTier:N1} mies." : "—";
        CumulativeDisplay = tier.MonthsCumulative > 0 ? $"łącznie {tier.MonthsCumulative:N1} mies." : "";
        TierValueDisplay  = tier.TierValue.Grosze > 0
            ? $"{tier.TierValue.Grosze / 100m:N2} zł"
            : "brak aktywów";
        HasAssets         = tier.Assets.Count > 0;
        Assets            = tier.Assets.Select(r => new AssetRowVm(r, updateCommand)).ToList();
    }
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class AssetsViewModel : ObservableObject
{
    private readonly GetCushionOverviewUseCase _overview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    [NotifyPropertyChangedFor(nameof(TotalMonthsDisplay))]
    [NotifyPropertyChangedFor(nameof(AvgExpenseDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalValueDisplay))]
    private CushionOverview? _cushion;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ObservableCollection<CushionTierVm> _tiers = [];

    public bool IsEmpty    => !Tiers.Any(t => t.HasAssets);
    public bool IsNotEmpty => !IsEmpty;

    public string TotalMonthsDisplay => Cushion is not null && Cushion.TotalMonths > 0
        ? $"{Cushion.TotalMonths:N1} mies."
        : "—";

    public string AvgExpenseDisplay => Cushion is not null && Cushion.MonthsOfData > 0
        ? $"śr. wydatki: {Cushion.AvgMonthlyExpense.Grosze / 100m:N2} zł / mies. (z {Cushion.MonthsOfData} mies.)"
        : "brak danych o wydatkach";

    public string TotalValueDisplay => Cushion is not null
        ? $"razem: {Cushion.TotalValue.Grosze / 100m:N2} zł"
        : "";

    public AssetsViewModel(GetCushionOverviewUseCase overview)
    {
        _overview = overview;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Cushion = await _overview.ExecuteAsync(ct: ct);

            var updateCmd = new AsyncRelayCommand<AssetId>(async id =>
            {
                // Wiersze kont rozliczeniowych są tylko do odczytu (saldo z rozliczeń).
                if (id == default) return;
                await Shell.Current.GoToAsync($"updateAssetValue?assetId={id}");
            });

            Tiers = new ObservableCollection<CushionTierVm>(
                Cushion.Tiers.Select(t => new CushionTierVm(t, updateCmd)));

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsNotEmpty));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAssetAsync() =>
        await Shell.Current.GoToAsync("addAsset");
}
