using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

/// <summary>Co zrobić z resztą salda zamykanego funduszu.</summary>
public enum RemainderPlan
{
    /// <summary>Przenieść na inny, już istniejący cel.</summary>
    ToExistingFund,
    /// <summary>Założyć nowy cel i przenieść resztę na niego — typowo odnowienie na kolejny rok.</summary>
    ToNewFund,
    /// <summary>Zostawić: pieniądze przestają być odłożone i wracają do puli wolnych.</summary>
    Release,
}

public sealed record RemainderPlanItem(RemainderPlan Plan, string Display);

/// <summary>
/// Zakończenie funduszu. Osobny ekran, a nie okienko z pytaniem, bo decyzja o reszcie
/// salda ma trzy wyjścia, a jedno z nich (nowy cel) potrzebuje kilku pól.
/// </summary>
[QueryProperty(nameof(FundId), "fundId")]
public partial class CloseFundViewModel : ObservableObject
{
    private readonly IFundRepository _funds;
    private readonly CloseFundUseCase _close;
    private readonly CreateFundUseCase _create;

    private Castellan.Domain.FundId? _id;

    [ObservableProperty] private string _fundId = "";
    [ObservableProperty] private string _name = "—";
    [ObservableProperty] private string _balanceDisplay = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Pusty fundusz nie ma o co pytać — zostaje samo potwierdzenie. Pytanie o resztę,
    /// której nie ma, każe tylko wybierać między trzema równie pustymi opcjami.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemainder))]
    private bool _isEmptyFund;

    public bool HasRemainder => !IsEmptyFund;

    public ObservableCollection<RemainderPlanItem> Plans { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MovesToExistingFund))]
    [NotifyPropertyChangedFor(nameof(CreatesNewFund))]
    [NotifyPropertyChangedFor(nameof(ReleasesRemainder))]
    private RemainderPlanItem? _selectedPlan;

    public bool MovesToExistingFund => SelectedPlan?.Plan == RemainderPlan.ToExistingFund;
    public bool CreatesNewFund      => SelectedPlan?.Plan == RemainderPlan.ToNewFund;
    public bool ReleasesRemainder   => SelectedPlan?.Plan == RemainderPlan.Release;

    public ObservableCollection<FundChoice> Targets { get; } = [];

    [ObservableProperty] private FundChoice? _target;

    // Pola nowego funduszu — wypełniane parametrami kończonego, bo najczęstszy powód
    // zakładania celu przy zamykaniu to odnowienie tego samego na kolejny okres.
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private FundKindItem? _newKind;
    [ObservableProperty] private string _newTargetText = "";
    [ObservableProperty] private DateTime _newDeadline = DateTime.Today.AddYears(1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewHasDeadline))]
    private bool _newIsOpenEnded;

    public bool NewHasDeadline => !NewIsOpenEnded;

    public List<FundKindItem> Kinds { get; } =
    [
        new(FundKind.Tax,       "Podatki"),
        new(FundKind.Insurance, "Ubezpieczenie"),
        new(FundKind.Vacation,  "Urlop"),
        new(FundKind.Emergency, "Poduszka bezpieczeństwa"),
        new(FundKind.Custom,    "Inny"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public CloseFundViewModel(IFundRepository funds, CloseFundUseCase close, CreateFundUseCase create)
    {
        _funds = funds;
        _close = close;
        _create = create;
    }

    partial void OnFundIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new Castellan.Domain.FundId(guid);
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;

        var all = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        var fund = all.FirstOrDefault(f => f.Id == id);
        if (fund is null) return;

        Name = fund.Name;
        BalanceDisplay = fund.Balance.ToString();
        IsEmptyFund = fund.Balance.Grosze == 0;

        Targets.Clear();
        foreach (var f in all.Where(f => f.Id != id))
            Targets.Add(new FundChoice(f.Id, f.Name, f.Balance.ToString()));
        Target = Targets.FirstOrDefault();

        NewName = fund.Name;
        NewKind = Kinds.FirstOrDefault(k => k.Kind == fund.Kind) ?? Kinds[^1];
        NewTargetText = (fund.TargetAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
        NewIsOpenEnded = fund.Deadline is null;
        // Rok od starego terminu: fundusz z terminem zamyka się zwykle wtedy, gdy rzecz
        // została opłacona, a kolejna taka sama przychodzi za rok.
        NewDeadline = fund.Deadline is { } d
            ? d.AddYears(1).ToDateTime(TimeOnly.MinValue)
            : DateTime.Today.AddYears(1);

        Plans.Clear();
        // Bez drugiego aktywnego funduszu nie ma dokąd przenieść — opcja, która zawsze
        // kończy się błędem, nie powinna stać na liście.
        if (Targets.Count > 0)
            Plans.Add(new RemainderPlanItem(RemainderPlan.ToExistingFund, "Przenieś na inny fundusz"));
        Plans.Add(new RemainderPlanItem(RemainderPlan.ToNewFund, "Załóż nowy fundusz i przenieś tam"));
        Plans.Add(new RemainderPlanItem(RemainderPlan.Release, "Zostaw — przestanie być odłożone"));
        SelectedPlan = Plans[0];
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        IsBusy = true;
        try
        {
            Castellan.Domain.FundId? destination = null;

            if (HasRemainder)
            {
                switch (SelectedPlan?.Plan)
                {
                    case RemainderPlan.ToExistingFund:
                        if (Target is null) { ErrorMessage = "Wybierz fundusz, na który ma pójść reszta."; return; }
                        destination = Target.Id;
                        break;

                    case RemainderPlan.ToNewFund:
                        if (!TryBuildNewFund(out var cmd)) return;
                        destination = await _create.ExecuteAsync(cmd, ct);
                        break;

                    case RemainderPlan.Release:
                        break;

                    default:
                        ErrorMessage = "Wybierz, co zrobić z resztą.";
                        return;
                }
            }

            await _close.ExecuteAsync(id, destination, ct);
            if (Shell.Current is { } shell) await shell.GoToAsync("..");
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

    private bool TryBuildNewFund(out CreateFundCommand cmd)
    {
        cmd = null!;

        if (string.IsNullOrWhiteSpace(NewName)) { ErrorMessage = "Podaj nazwę nowego funduszu."; return false; }
        if (NewKind is null)                    { ErrorMessage = "Wybierz rodzaj nowego funduszu."; return false; }

        var target = ParseGrosze(NewTargetText);
        if (target <= 0) { ErrorMessage = "Podaj poprawną kwotę docelową nowego funduszu."; return false; }

        DateOnly? deadline = null;
        if (!NewIsOpenEnded)
        {
            var picked = DateOnly.FromDateTime(NewDeadline);
            if (picked <= DateOnly.FromDateTime(DateTime.Today))
            {
                ErrorMessage = "Termin nowego funduszu musi być w przyszłości.";
                return false;
            }
            deadline = picked;
        }

        cmd = new CreateFundCommand(NewName.Trim(), NewKind.Kind, new Money(target), deadline);
        return true;
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");

    private static long ParseGrosze(string text)
    {
        var normalized = text.Trim().Replace(',', '.').Replace(" ", "");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0)
            return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
        return 0;
    }
}
