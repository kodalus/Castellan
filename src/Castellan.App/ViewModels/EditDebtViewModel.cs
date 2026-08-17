using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(DebtIdParam), "debtId")]
public partial class EditDebtViewModel : ObservableObject
{
    private readonly IDebtRepository _debts;
    private readonly UpdateDebtUseCase _update;

    private DebtId? _id;

    [ObservableProperty] private string _debtIdParam = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private DebtKindItem? _selectedKind;
    [ObservableProperty] private string _initialAmountText = "";
    [ObservableProperty] private string _balanceText = "";
    [ObservableProperty] private string _installmentText = "";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public List<DebtKindItem> Kinds { get; } =
    [
        new(DebtKind.CashLoan,    "Kredyt gotówkowy"),
        new(DebtKind.Mortgage,    "Kredyt hipoteczny"),
        new(DebtKind.Installment, "Zakup na raty"),
        new(DebtKind.FromFamily,  "Pożyczka od bliskich"),
        new(DebtKind.Other,       "Inne zobowiązanie"),
    ];

    public EditDebtViewModel(IDebtRepository debts, UpdateDebtUseCase update)
    {
        _debts = debts;
        _update = update;
    }

    partial void OnDebtIdParamChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new DebtId(guid);
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;

        var debt = await _debts.GetAsync(id, ct);
        if (debt is null) return;

        Name = debt.Name;
        SelectedKind = Kinds.FirstOrDefault(k => k.Kind == debt.Kind) ?? Kinds[0];
        InitialAmountText = (debt.InitialAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
        BalanceText = (debt.Balance.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
        InstallmentText = (debt.InstallmentAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Podaj nazwę zobowiązania."; return; }
        if (SelectedKind is null)            { ErrorMessage = "Wybierz rodzaj."; return; }

        var initial = ParseGrosze(InitialAmountText);
        if (initial <= 0) { ErrorMessage = "Podaj kwotę początkową."; return; }

        var balance = ParseGrosze(BalanceText, allowZero: true);
        if (balance < 0) { ErrorMessage = "Saldo nie może być ujemne."; return; }

        var installment = ParseGrosze(InstallmentText);

        IsBusy = true;
        try
        {
            await _update.ExecuteAsync(new UpdateDebtCommand(
                id, Name.Trim(), SelectedKind.Kind,
                new Money(initial), new Money(installment), new Money(balance)), ct);
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

    private static long ParseGrosze(string text, bool allowZero = false)
    {
        var normalized = text.Trim().Replace(',', '.').Replace(" ", "");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            && (d > 0 || (allowZero && d == 0)))
            return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
        return 0;
    }
}
