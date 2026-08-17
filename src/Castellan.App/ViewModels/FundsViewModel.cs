using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
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
    ICommand ContributeCommand,
    ICommand EditCommand,
    ICommand DeleteCommand)
{
    public bool IsNotDelayed => !IsDelayed;
}

public partial class FundsViewModel : ObservableObject
{
    private readonly GetFundOverviewUseCase _overview;
    private readonly DeleteFundUseCase _deleteFund;

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

    [ObservableProperty] private string _totalSuggestedDisplay = "";
    [ObservableProperty] private string _totalBalanceDisplay = "";

    public bool NeedsPaydate => PaydateDay <= 0;

    public FundsViewModel(GetFundOverviewUseCase overview, DeleteFundUseCase deleteFund)
    {
        _overview = overview;
        _deleteFund = deleteFund;
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
            var overview = await _overview.ExecuteAsync(PaydateDay, ct);
            TotalSuggestedDisplay = $"Odkładasz łącznie: {overview.TotalSuggestedMonthly} / mies.";
            TotalBalanceDisplay   = $"Zebrane w funduszach: {overview.TotalBalance}";
            foreach (var s in overview.Items)
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
                        Shell.Current.GoToAsync($"contributeFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"editFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() => DeleteFundAsync(fundId, s.Name, s.Balance))));
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

    private async Task DeleteFundAsync(FundId id, string name, Money balance)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        try
        {
            var linked = await _deleteFund.CountLinkedTransactionsAsync(id);

            // Uczciwie wypisz konsekwencje: zebrane saldo znika z ewidencji, a wydatki
            // pokryte z tego funduszu wrócą do kopert i znów obciążą budżet miesiąca.
            var warnings = new List<string>();
            if (balance.Grosze != 0)
                warnings.Add($"Zebrane {balance} przestanie być widoczne w Majątku.");
            if (linked > 0)
                warnings.Add($"{linked} transakcji pokrytych z tego funduszu wróci do kopert i znów obciąży budżet.");

            var details = warnings.Count > 0
                ? "\n\n" + string.Join("\n\n", warnings)
                : "";

            var confirmed = await page.DisplayAlertAsync(
                $"Usunąć „{name}”?",
                $"Tej operacji nie można cofnąć.{details}",
                "Usuń", "Anuluj");
            if (!confirmed) return;

            await _deleteFund.ExecuteAsync(id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            await page.DisplayAlertAsync("Błąd usuwania funduszu", sb.ToString(), "OK");
        }
    }
}
