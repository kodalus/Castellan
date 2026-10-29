using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

/// <summary>Wiersz listy funduszy.</summary>
public sealed class FundRow
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
    public ICommand MoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoreCommand { get; }

    public bool IsNotDelayed => !IsDelayed;

    public FundRow(
        FundId id, string name, string kindDisplay, string balanceDisplay, string targetDisplay,
        string suggestedMonthlyDisplay, string periodsRemainingDisplay, string deadlineDisplay,
        string deficitDisplay, bool isDelayed, double progress,
        ICommand contributeCommand, ICommand moveCommand,
        ICommand editCommand, ICommand deleteCommand, ICommand moreCommand)
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
        MoveCommand = moveCommand;
        EditCommand = editCommand;
        DeleteCommand = deleteCommand;
        MoreCommand = moreCommand;
    }
}

/// <summary>
/// Zakończony fundusz. Został na liście, żeby „zakończ” nie wyglądało jak „usuń”:
/// widać, ile mu zostało w chwili zamknięcia, i da się go przywrócić jednym tapnięciem.
/// </summary>
public sealed record ClosedFundRow(
    string Name,
    string KindDisplay,
    string BalanceDisplay,
    ICommand ReopenCommand,
    ICommand DeleteCommand);

public partial class FundsViewModel : ObservableObject
{
    private readonly GetFundOverviewUseCase _overview;
    private readonly DeleteFundUseCase _deleteFund;
    private readonly CloseFundUseCase _closeFund;

    public ObservableCollection<FundRow> Items { get; } = [];
    public ObservableCollection<ClosedFundRow> Closed { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    /// <summary>
    /// „Brak funduszy. Dodaj pierwszy” jest nieprawdą, gdy wszystkie fundusze są
    /// zakończone — wtedy lista zakończonych mówi sama za siebie.
    /// </summary>
    public bool ShowEmptyState => IsEmpty && !HasClosed;

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
        CloseFundUseCase closeFund)
    {
        _overview = overview;
        _deleteFund = deleteFund;
        _closeFund = closeFund;
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
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"contributeFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"moveFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"editFund?fundId={fundId.Value}")),
                    new AsyncRelayCommand(() => DeleteFundAsync(fundId, s.Name, s.Balance)),
                    new AsyncRelayCommand(() => MoreAsync(fundId, s.Name, s.Balance))));
            }
            IsEmpty = Items.Count == 0;

            Closed.Clear();
            foreach (var c in overview.Closed)
            {
                var closedId = c.Id;
                Closed.Add(new ClosedFundRow(
                    c.Name,
                    c.KindDisplay,
                    c.Balance.Grosze > 0 ? $"Zostało: {c.Balance}" : "Wyzerowany",
                    new AsyncRelayCommand(() => ReopenFundAsync(closedId, c.Name)),
                    new AsyncRelayCommand(() => DeleteFundAsync(closedId, c.Name, c.Balance))));
            }
            HasClosed = Closed.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Funds.Load] " + ex);
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

    /// <summary>
    /// Edycja, zakończenie i usunięcie pod jednym przyciskiem. Na telefonie były
    /// wyłącznie pod przeciągnięciem wiersza, czyli pod gestem, którego nic nie zapowiada
    /// — a trzeci przycisk w wierszu i tak by się nie zmieścił.
    /// </summary>
    private async Task MoreAsync(FundId id, string name, Money balance)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        var choice = await page.DisplayActionSheetAsync(
            name, "Anuluj", null, "Edytuj", "Zakończ fundusz", "Usuń");

        switch (choice)
        {
            case "Edytuj":
                await Shell.Current.GoToAsync($"editFund?fundId={id.Value}");
                break;
            case "Zakończ fundusz":
                await Shell.Current.GoToAsync($"closeFund?fundId={id.Value}");
                break;
            case "Usuń":
                await DeleteFundAsync(id, name, balance);
                break;
        }
    }

    private async Task ReopenFundAsync(FundId id, string name)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        try
        {
            await _closeFund.ReopenAsync(id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync($"Nie udało się przywrócić „{name}”", ex.Message, "OK");
        }
    }

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
                warnings.Add($"Ślad po zebranych {balance} zniknie — same pieniądze zostają na koncie, "
                             + "ale nic już nie będzie pamiętało, że były odłożone na ten cel.");
            if (linked > 0)
                warnings.Add($"{linked} transakcji pokrytych z tego funduszu wróci do kopert i znów obciąży budżet.");

            var details = warnings.Count > 0
                ? "\n\n" + string.Join("\n\n", warnings)
                : "";

            var confirmed = await page.DisplayAlertAsync(
                $"Usunąć „{name}”?",
                $"Tej operacji nie można cofnąć. Jeśli cel się po prostu wydarzył, wybierz "
                + $"zamiast tego „Zakończ fundusz” — to zachowuje historię.{details}",
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
