using System.Globalization;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record DebtKindItem(DebtKind Kind, string Display);

public partial class AddDebtViewModel : ObservableObject
{
    private readonly CreateDebtUseCase _create;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private DebtKindItem? _selectedKind;
    [ObservableProperty] private string _initialAmountText = "";
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

    public AddDebtViewModel(CreateDebtUseCase create)
    {
        _create = create;
        SelectedKind = Kinds[0];
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Podaj nazwę zobowiązania."; return; }
        if (SelectedKind is null)            { ErrorMessage = "Wybierz rodzaj."; return; }

        var initial = ParseGrosze(InitialAmountText);
        if (initial <= 0) { ErrorMessage = "Podaj kwotę pozostałą do spłaty."; return; }

        // Rata może być pusta — np. pożyczka od rodziny bez ustalonego harmonogramu.
        var installment = ParseGrosze(InstallmentText);

        IsBusy = true;
        try
        {
            await _create.ExecuteAsync(new CreateDebtCommand(
                Name.Trim(), SelectedKind.Kind, new Money(initial), new Money(installment)), ct);
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
