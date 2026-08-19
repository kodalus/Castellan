using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

/// <summary>
/// Wiersz listy funduszy. Klasa obserwowalna, a nie rekord, bo znacznik „licz do
/// poduszki" przełącza się wprost tutaj — Switch potrzebuje wiązania dwustronnego.
/// </summary>
public sealed partial class FundRow : ObservableObject
{
    public FundId Id { get; }
    public string Name { get; }
    public string KindDisplay { get; }
    public string BalanceDisplay { get; }
    public string TargetDisplay { get; }
    public string SuggestedMonthlyDisplay { get; }
    public string PeriodsRemainingDisplay { get; }
    public string DeadlineDisplay { get; }
    public string DeficitDisplay { get; }
    public bool IsDelayed { get; }
    public double Progress { get; }
    public ICommand ContributeCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public bool IsNotDelayed => !IsDelayed;

    private readonly Func<bool, Task> _onCushionChanged;
    private readonly bool _ready;

    [ObservableProperty] private bool _countsTowardCushion;

    // Wartość początkową ustawiamy przed uzbrojeniem, bo inaczej samo zbudowanie
    // listy zapisywałoby do bazy każdy wiersz z osobna przy każdym odświeżeniu.
    partial void OnCountsTowardCushionChanged(bool value)
    {
        if (_ready) _ = _onCushionChanged(value);
    }

    public FundRow(
        FundId id, string name, string kindDisplay, string balanceDisplay, string targetDisplay,
        string suggestedMonthlyDisplay, string periodsRemainingDisplay, string deadlineDisplay,
        string deficitDisplay, bool isDelayed, double progress, bool countsTowardCushion,
        ICommand contributeCommand, ICommand editCommand, ICommand deleteCommand,
        Func<bool, Task> onCushionChanged)
    {
        Id = id;
        Name = name;
        KindDisplay = kindDisplay;
        BalanceDisplay = balanceDisplay;
        TargetDisplay = targetDisplay;
        SuggestedMonthlyDisplay = suggestedMonthlyDisplay;
        PeriodsRemainingDisplay = periodsRemainingDisplay;
        DeadlineDisplay = deadlineDisplay;
        DeficitDisplay = deficitDisplay;
        IsDelayed = isDelayed;
        Progress = progress;
        ContributeCommand = contributeCommand;
        EditCommand = editCommand;
        DeleteCommand = deleteCommand;

        _countsTowardCushion = countsTowardCushion;
        _onCushionChanged = onCushionChanged;
        _ready = true;
    }
}

public partial class FundsViewModel : ObservableObject
{
    private readonly GetFundOverviewUseCase _overview;
    private readonly DeleteFundUseCase _deleteFund;
    private readonly SetFundCushionFlagUseCase _setCushionFlag;

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

    public FundsViewModel(
        GetFundOverviewUseCase overview,
        DeleteFundUseCase deleteFund,
        SetFundCushionFlagUseCase setCushionFlag)
    {
        _overview = overview;
        _deleteFund = deleteFund;
        _setCushionFlag = setCushionFlag;
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
                    // Fundusz otwarty nie ma raty do podpowiedzenia, więc zamiast
                    // „Wpłać co miesiąc: 0,00 zł" pokazuje, ile brakuje do celu.
                    s.IsOpenEnded
                        ? $"Do celu brakuje: {Remaining(s)}"
                        : $"Wpłać co miesiąc: {s.SuggestedMonthly}",
                    s.IsOpenEnded
                        ? "Bez terminu — zbierasz, aż uzbiera"
                        : s.PeriodsRemaining > 0
                            ? $"{s.PeriodsRemaining} rat do {s.Deadline:MM/yyyy}"
                            : $"Termin: {s.Deadline:MM/yyyy}",
                    s.Deadline?.ToString("MM/yyyy") ?? "",
                    // Bez terminu nie ma tempa, więc ani „brakuje", ani „na bieżąco"
                    // nic by nie znaczyło — pole zostaje puste.
                    s.IsOpenEnded ? "" : s.IsDelayed ? $"⚠ Brakuje {s.Deficit}" : "✓ Na bieżąco",
                    s.IsDelayed,
                    s.Progress,
                    s.CountsTowardCushion,
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"contributeFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"editFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() => DeleteFundAsync(fundId, s.Name, s.Balance)),
                    counts => ToggleCushionAsync(fundId, counts)));
            }
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Funds.Load] " + ex);
        }
    }

    /// <summary>
    /// Zapisuje przestawiony znacznik. Lista nie jest przeładowywana — wiersz już
    /// pokazuje nowy stan, a przeładowanie w trakcie przesuwania przełącznika
    /// wyrzuciłoby użytkownika na górę listy.
    /// </summary>
    private async Task ToggleCushionAsync(FundId id, bool counts)
    {
        try
        {
            await _setCushionFlag.ExecuteAsync(id, counts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Funds.ToggleCushion] " + ex);
        }
    }

    private static Money Remaining(FundSummary s) =>
        new(Math.Max(0, s.TargetAmount.Grosze - s.Balance.Grosze));

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
