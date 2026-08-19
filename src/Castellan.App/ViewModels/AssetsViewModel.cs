using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.Repositories;
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

public sealed class FundRowVm
{
    public string Name  { get; }
    public string ValueDisplay { get; }

    /// <summary>
    /// Fundusze wliczone do poduszki zostają na tej liście, tylko z dopiskiem. Wcześniej
    /// z niej znikały (żeby nie zostać policzone dwa razy w wartości netto) i wychodziło
    /// z tego coś odwrotnego do napisu na przełączniku: zaznaczenie „licz do poduszki"
    /// kasowało fundusz z jedynej listy funduszy, jaką widać na tym ekranie.
    /// </summary>
    public string CushionNote { get; }
    public bool HasCushionNote => CushionNote.Length > 0;

    public FundRowVm(string name, Money balance, bool countsTowardCushion)
    {
        Name = name;
        ValueDisplay = $"{balance.Grosze / 100m:N2} zł";
        CushionNote = countsTowardCushion ? "policzone wyżej w poduszce" : "";
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
    private readonly IFundRepository _funds;
    private readonly GetDebtOverviewUseCase _debtOverview;
    private readonly DeleteDebtUseCase _deleteDebt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    [NotifyPropertyChangedFor(nameof(TotalMonthsDisplay))]
    [NotifyPropertyChangedFor(nameof(AvgExpenseDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalValueDisplay))]
    private CushionOverview? _cushion;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ObservableCollection<CushionTierVm> _tiers = [];
    [ObservableProperty] private ObservableCollection<FundRowVm> _fundRows = [];
    [ObservableProperty] private string _fundsTotalDisplay = "";
    [ObservableProperty] private string _fundsCushionNoteDisplay = "";
    [ObservableProperty] private bool _hasFunds;

    [ObservableProperty] private ObservableCollection<DebtRowVm> _debtRows = [];
    [ObservableProperty] private string _debtsTotalDisplay = "";
    [ObservableProperty] private string _debtsInstallmentsDisplay = "";
    [ObservableProperty] private bool _hasDebts;

    [ObservableProperty] private string _netWorthDisplay = "";
    [ObservableProperty] private bool _isNetWorthNegative;

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
        IFundRepository funds,
        GetDebtOverviewUseCase debtOverview,
        DeleteDebtUseCase deleteDebt)
    {
        _overview = overview;
        _funds = funds;
        _debtOverview = debtOverview;
        _deleteDebt = deleteDebt;
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

            // Lista pokazuje WSZYSTKIE aktywne fundusze, także te wliczone do poduszki —
            // te dostają dopisek, że policzono je wyżej. Ukrywanie ich sprawiało, że
            // przełącznik „licz do poduszki" wyglądał na odwrócony: zaznaczenie kasowało
            // fundusz z listy funduszy zamiast go gdziekolwiek dodać.
            var activeFunds = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
            FundRows = new ObservableCollection<FundRowVm>(
                activeFunds.Select(f => new FundRowVm(f.Name, f.Balance, f.CountsTowardCushion)));
            HasFunds = activeFunds.Count > 0;

            // Do wartości netto wchodzą tylko fundusze spoza poduszki — te wliczone
            // siedzą już w Cushion.TotalValue, więc dodanie ich tutaj podwoiłoby kwotę.
            var fundsOutsideCushion = activeFunds.Where(f => !f.CountsTowardCushion).Sum(f => f.Balance.Grosze);
            var fundsInCushion      = activeFunds.Where(f => f.CountsTowardCushion).Sum(f => f.Balance.Grosze);

            FundsTotalDisplay = $"razem: {(fundsOutsideCushion + fundsInCushion) / 100m:N2} zł";
            FundsCushionNoteDisplay = fundsInCushion > 0
                ? $"w tym {fundsInCushion / 100m:N2} zł policzone w poduszce"
                : "";

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

            // Wartość netto = aktywa + fundusze − długi. Fundusze to realne pieniądze
            // odłożone na bok, więc wchodzą do majątku, mimo że są poza poduszką.
            var netGrosze = (Cushion?.TotalValue.Grosze ?? 0) + fundsOutsideCushion - debts.TotalBalance.Grosze;
            IsNetWorthNegative = netGrosze < 0;
            NetWorthDisplay = new Money(netGrosze).ToString();
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
