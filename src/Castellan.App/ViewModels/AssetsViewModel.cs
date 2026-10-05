using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
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
    public ICommand DeleteCommand { get; }

    /// <summary>
    /// Wiersze kont i funduszy trafiają do tej samej listy co aktywa, ale nie są
    /// aktywami — nie mają własnego Id i nie wolno ich stąd usuwać. Saldo konta bierze
    /// się z transakcji, a fundusz ma własną zakładkę.
    /// </summary>
    public bool IsAccount { get; }
    public bool IsDeletable => !IsAccount;

    /// <summary>
    /// Przycisk usuwania w wierszu — tylko na pulpicie i tylko tam, gdzie jest co usuwać.
    /// Dwa warunki naraz, więc nie da się tego zapisać samym „{OnPlatform}" w XAML-u:
    /// IsVisible przyjmuje jedno źródło. A wyszarzony przycisk przy saldzie konta
    /// obiecywałby działanie, którego tam nigdy nie będzie.
    /// </summary>
    public bool ShowDeleteButton => IsDeletable && !OperatingSystem.IsAndroid();

    public AssetRowVm(AssetRow row, ICommand updateCommand, ICommand deleteCommand)
    {
        Id             = row.Id;
        Name           = row.Name;
        IsAccount      = row.IsAccount;
        ValueDisplay   = $"{row.Value.Grosze / 100m:N2} zł";
        UpdatedDisplay = row.IsAccount ? "saldo konta" : row.UpdatedOn.ToString("d.MM.yyyy");
        UpdateCommand  = updateCommand;
        DeleteCommand  = deleteCommand;
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

    public CushionTierVm(CushionTier tier, ICommand updateCommand, ICommand deleteCommand)
    {
        LiquidityDisplay  = tier.LiquidityDisplay;
        MonthsTierDisplay = tier.MonthsTier > 0 ? $"{tier.MonthsTier:N1} mies." : "—";
        CumulativeDisplay = tier.MonthsCumulative > 0 ? $"łącznie {tier.MonthsCumulative:N1} mies." : "";
        TierValueDisplay  = tier.TierValue.Grosze > 0
            ? $"{tier.TierValue.Grosze / 100m:N2} zł"
            : "brak aktywów";
        HasAssets         = tier.Assets.Count > 0;
        Assets            = tier.Assets.Select(r => new AssetRowVm(r, updateCommand, deleteCommand)).ToList();
    }
}

public sealed class DebtRowVm
{
    public DebtId Id { get; }
    public string Name { get; }
    public string KindDisplay { get; }
    public string BalanceDisplay { get; }
    public string PaidOffDisplay { get; }
    public string InstallmentDisplay { get; }
    public string PayoffDisplay { get; }
    public double Progress { get; }
    public bool IsPaidOff { get; }
    public bool IsNotPaidOff => !IsPaidOff;
    public ICommand PayCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public DebtRowVm(DebtSummary d, ICommand pay, ICommand edit, ICommand delete)
    {
        Id = d.Id;
        Name = d.Name;
        KindDisplay = d.KindDisplay;
        BalanceDisplay = d.Balance.ToString();
        PaidOffDisplay = $"spłacone {d.PaidOff} z {d.InitialAmount}";
        InstallmentDisplay = d.InstallmentAmount.Grosze > 0
            ? $"Rata: {d.InstallmentAmount}"
            : "Brak ustalonej raty";
        // Bez raty nie da się uczciwie podać terminu — lepiej powiedzieć to wprost,
        // niż pokazać wymyśloną datę.
        PayoffDisplay = d.IsPaidOff
            ? "✓ Spłacone"
            : d.ProjectedPayoff is { } p && d.InstallmentsRemaining is { } n
                ? $"{n} rat, do {p:MM/yyyy}"
                : "Termin nieznany — podaj ratę";
        Progress = d.Progress;
        IsPaidOff = d.IsPaidOff;
        PayCommand = pay;
        EditCommand = edit;
        DeleteCommand = delete;
    }
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class AssetsViewModel : ObservableObject
{
    private readonly GetCushionOverviewUseCase _overview;
    private readonly GetDebtOverviewUseCase _debtOverview;
    private readonly DeleteDebtUseCase _deleteDebt;
    private readonly DeleteAssetUseCase _deleteAsset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    [NotifyPropertyChangedFor(nameof(TotalMonthsDisplay))]
    [NotifyPropertyChangedFor(nameof(AvgExpenseDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalValueDisplay))]
    private CushionOverview? _cushion;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ObservableCollection<CushionTierVm> _tiers = [];

    [ObservableProperty] private ObservableCollection<DebtRowVm> _debtRows = [];
    [ObservableProperty] private string _debtsTotalDisplay = "";
    [ObservableProperty] private string _debtsInstallmentsDisplay = "";
    [ObservableProperty] private bool _hasDebts;

    [ObservableProperty] private string _netWorthDisplay = "";
    [ObservableProperty] private bool _isNetWorthNegative;

    // Sam wynik bez składników wygląda przy dużym kredycie na błąd aplikacji,
    // a nie na brakujące aktywo po drugiej stronie.
    [ObservableProperty] private string _netWorthAssetsDisplay = "";
    [ObservableProperty] private string _netWorthDebtsDisplay = "";

    [ObservableProperty] private string _mortgageHintDisplay = "";
    [ObservableProperty] private bool _hasMortgageHint;

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

    public AssetsViewModel(
        GetCushionOverviewUseCase overview,
        GetDebtOverviewUseCase debtOverview,
        DeleteDebtUseCase deleteDebt,
        DeleteAssetUseCase deleteAsset)
    {
        _overview = overview;
        _debtOverview = debtOverview;
        _deleteDebt = deleteDebt;
        _deleteAsset = deleteAsset;
    }

    /// <summary>
    /// Usuwa aktywo po potwierdzeniu. Komunikat podaje kwotę, bo skutek jest natychmiast
    /// widoczny na tym samym ekranie: poduszka finansowa maleje o tę wartość.
    /// </summary>
    private async Task DeleteAssetAsync(AssetRowVm? row)
    {
        if (row is null) return;
        if (Shell.Current?.CurrentPage is not Page page) return;

        // Wiersz konta wygląda jak aktywo, ale nim nie jest. Zamiast milczeć, powiedz
        // gdzie to zmienić — inaczej wygląda to na zepsuty przycisk.
        if (row.IsAccount)
        {
            await page.DisplayAlertAsync(
                "To nie jest aktywo",
                "Ten wiersz to saldo konta, liczone z transakcji. Konto usuniesz w zakładce Konta.",
                "OK");
            return;
        }

        try
        {
            var confirmed = await page.DisplayAlertAsync(
                $"Usunąć „{row.Name}”?",
                $"Poduszka finansowa zmniejszy się o {row.ValueDisplay}. Tej operacji nie można cofnąć.",
                "Usuń", "Anuluj");
            if (!confirmed) return;

            await _deleteAsset.ExecuteAsync(row.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            await page.DisplayAlertAsync("Błąd usuwania aktywa", sb.ToString(), "OK");
        }
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

            var deleteCmd = new AsyncRelayCommand<AssetRowVm>(DeleteAssetAsync);

            Tiers = new ObservableCollection<CushionTierVm>(
                Cushion.Tiers.Select(t => new CushionTierVm(t, updateCmd, deleteCmd)));

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsNotEmpty));

            var debts = await _debtOverview.ExecuteAsync(ct);
            DebtRows = new ObservableCollection<DebtRowVm>(debts.Items.Select(d =>
            {
                var captured = d;
                return new DebtRowVm(
                    d,
                    new AsyncRelayCommand(() => Shell.Current.GoToAsync($"payDebt?debtId={captured.Id.Value}")),
                    new AsyncRelayCommand(() => Shell.Current.GoToAsync($"editDebt?debtId={captured.Id.Value}")),
                    new AsyncRelayCommand(() => DeleteDebtAsync(captured)));
            }));
            HasDebts = debts.Items.Count > 0;
            DebtsTotalDisplay = $"razem: {debts.TotalBalance}";
            DebtsInstallmentsDisplay = debts.TotalMonthlyInstallments.Grosze > 0
                ? $"raty: {debts.TotalMonthlyInstallments} / mies."
                : "";

            // Wartość netto = (aktywa + salda kont) − całe salda zobowiązań, nie raty.
            // Rata nie jest tu alternatywą: „majątek minus jedna rata” nie odpowiadałoby
            // na żadne pytanie. Jeśli kredyt coś kupił (mieszkanie, auto), tę rzecz dodaje
            // się po drugiej stronie jako aktywo — inaczej liczona jest połowa transakcji.
            var assetsGrosze = Cushion?.TotalValue.Grosze ?? 0;
            var netGrosze = assetsGrosze - debts.TotalBalance.Grosze;
            IsNetWorthNegative = netGrosze < 0;
            NetWorthDisplay = new Money(netGrosze).ToString();
            NetWorthAssetsDisplay = $"aktywa i salda kont: {new Money(assetsGrosze)}";
            NetWorthDebtsDisplay = $"zobowiązania: −{debts.TotalBalance}";

            // Kredyt hipoteczny bez żadnego aktywa o płynności „Wolna" to prawie na pewno
            // zapisany dług bez zapisanej rzeczy, którą kupił. Warunek jest celowo wąski:
            // jedno takie aktywo wystarcza, żeby podpowiedź zniknęła i nie stała na stałe.
            var hasSlowAsset = Cushion?.Tiers
                .FirstOrDefault(t => t.Liquidity == AssetLiquidity.Slow)?.Assets.Count > 0;
            HasMortgageHint = debts.Items.Any(d => d.Kind == DebtKind.Mortgage && !d.IsPaidOff)
                              && hasSlowAsset != true;
            MortgageHintDisplay =
                "Masz kredyt hipoteczny, ale żadnego aktywa o płynności „Wolna”. "
                + "Dodaj mieszkanie jako aktywo — inaczej liczony jest sam dług bez rzeczy, "
                + "którą za niego kupiłaś.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteDebtAsync(DebtSummary debt)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        try
        {
            var confirmed = await page.DisplayAlertAsync(
                $"Usunąć „{debt.Name}”?",
                debt.Balance.Grosze > 0
                    ? $"Pozostałe {debt.Balance} zniknie z ewidencji zobowiązań. Zapłacone raty zostaną w historii transakcji."
                    : "Zapłacone raty zostaną w historii transakcji.",
                "Usuń", "Anuluj");
            if (!confirmed) return;

            await _deleteDebt.ExecuteAsync(debt.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            await page.DisplayAlertAsync("Błąd usuwania zobowiązania", sb.ToString(), "OK");
        }
    }

    [RelayCommand]
    private async Task AddDebtAsync() => await Shell.Current.GoToAsync("addDebt");

    [RelayCommand]
    private async Task AddAssetAsync() =>
        await Shell.Current.GoToAsync("addAsset");
}
