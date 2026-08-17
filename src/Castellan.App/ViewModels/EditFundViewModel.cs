using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(FundId), "fundId")]
public partial class EditFundViewModel : ObservableObject
{
    private readonly IFundRepository _funds;
    private readonly UpdateFundUseCase _update;

    private Castellan.Domain.FundId? _id;

    [ObservableProperty] private string _fundId = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private FundKindItem? _selectedKind;
    [ObservableProperty] private string _targetAmountText = "";
    [ObservableProperty] private DateTime _deadline = DateTime.Today.AddYears(1);
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _balanceDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public List<FundKindItem> Kinds { get; } =
    [
        new(FundKind.Tax,       "Podatki"),
        new(FundKind.Insurance, "Ubezpieczenie"),
        new(FundKind.Vacation,  "Urlop"),
        new(FundKind.Custom,    "Inny"),
    ];

    public EditFundViewModel(IFundRepository funds, UpdateFundUseCase update)
    {
        _funds = funds;
        _update = update;
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

        var fund = await _funds.GetAsync(id, ct);
        if (fund is null) return;

        Name = fund.Name;
        SelectedKind = Kinds.FirstOrDefault(k => k.Kind == fund.Kind) ?? Kinds[0];
        TargetAmountText = (fund.TargetAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
        Deadline = fund.Deadline.ToDateTime(TimeOnly.MinValue);
        // Saldo tylko do wglądu — zmienia się przez wpłaty i pokrywanie wydatków,
        // nie przez edycję parametrów funduszu.
        BalanceDisplay = $"Zebrane: {fund.Balance}";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Podaj nazwę funduszu."; return; }
        if (SelectedKind is null)            { ErrorMessage = "Wybierz rodzaj funduszu."; return; }

        var target = ParseGrosze(TargetAmountText);
        if (target <= 0) { ErrorMessage = "Podaj poprawną kwotę docelową."; return; }

        IsBusy = true;
        try
        {
            await _update.ExecuteAsync(
                new UpdateFundCommand(id, Name.Trim(), SelectedKind.Kind, new Money(target), DateOnly.FromDateTime(Deadline)), ct);
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
