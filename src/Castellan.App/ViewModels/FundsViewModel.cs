using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record FundRow(
    FundId Id,
    string Name,
    string KindDisplay,
    string BalanceDisplay,
    string TargetDisplay,
    string SuggestedMonthlyDisplay,
    string PeriodsRemainingDisplay,
    string DeadlineDisplay,
    string DeficitDisplay,
    bool IsDelayed,
    double Progress,
    ICommand ContributeCommand)
{
    public bool IsNotDelayed => !IsDelayed;
}

public partial class FundsViewModel : ObservableObject
{
    private readonly GetFundOverviewUseCase _overview;

    public ObservableCollection<FundRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    // Paydate setup
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsPaydate))]
    private int _paydateDay;

    [ObservableProperty] private string _paydateText = "";

    public bool NeedsPaydate => PaydateDay <= 0;

    public FundsViewModel(GetFundOverviewUseCase overview)
    {
        _overview = overview;
        PaydateDay = Microsoft.Maui.Storage.Preferences.Get("paydate_day", 0);
        PaydateText = PaydateDay > 0 ? PaydateDay.ToString() : "";
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            PaydateDay = Microsoft.Maui.Storage.Preferences.Get("paydate_day", 0);

            Items.Clear();
            var list = await _overview.ExecuteAsync(PaydateDay, ct);
            foreach (var s in list)
            {
                var fundId = s.Id;
                Items.Add(new FundRow(
                    fundId,
                    s.Name,
                    s.KindDisplay,
                    s.Balance.ToString(),
                    $"Cel: {s.TargetAmount}",
                    $"Wpłać co miesiąc: {s.SuggestedMonthly}",
                    s.PeriodsRemaining > 0
                        ? $"{s.PeriodsRemaining} rat do {s.Deadline:MM/yyyy}"
                        : $"Termin: {s.Deadline:MM/yyyy}",
                    s.Deadline.ToString("MM/yyyy"),
                    s.IsDelayed ? $"⚠ Brakuje {s.Deficit}" : "✓ Na bieżąco",
                    s.IsDelayed,
                    s.Progress,
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"contributeFund?fundId={fundId.Value}"))));
            }
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Funds.Load] " + ex);
        }
    }

    [RelayCommand]
    private void SavePaydate()
    {
        if (int.TryParse(PaydateText.Trim(), out var day) && day >= 1 && day <= 31)
        {
            PaydateDay = day;
            Microsoft.Maui.Storage.Preferences.Set("paydate_day", day);
            _ = LoadCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task AddFundAsync()
        => await Shell.Current.GoToAsync("addFund");
}
