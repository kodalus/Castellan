using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.App.Services;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class AddTransferViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;
    private readonly CreateTransferUseCase _createTransfer;

    public ObservableCollection<AccountOption> FromOptions { get; } = [];
    public ObservableCollection<AccountOption> ToOptions { get; } = [];

    [ObservableProperty] private int _fromIndex = -1;
    [ObservableProperty] private int _toIndex = -1;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public AddTransferViewModel(IAccountRepository accounts, CreateTransferUseCase createTransfer)
    {
        _accounts = accounts;
        _createTransfer = createTransfer;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        FromOptions.Clear();
        ToOptions.Clear();

        var list = await _accounts.ListAsync(ct);
        foreach (var a in list.Where(a => !a.IsArchived))
        {
            FromOptions.Add(new AccountOption(a.Id, a.Name));
            ToOptions.Add(new AccountOption(a.Id, a.Name));
        }

        // Przelew wychodzi zwykle z konta, którego używa się na co dzień.
        var preferred = DefaultAccountPreference.Get();
        FromIndex = FromOptions.Count == 0 ? -1
            : Math.Max(0, FromOptions.ToList().FindIndex(o => preferred is not null && o.Id == preferred));

        // Domyślnie inne konto niż źródłowe, żeby formularz od razu był poprawny.
        ToIndex = ToOptions.Count < 2 ? (ToOptions.Count == 1 ? 0 : -1)
            : (FromIndex == 0 ? 1 : 0);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        ErrorMessage = "";

        if (FromIndex < 0 || FromIndex >= FromOptions.Count) { ErrorMessage = "Wybierz konto źródłowe."; return; }
        if (ToIndex < 0 || ToIndex >= ToOptions.Count)       { ErrorMessage = "Wybierz konto docelowe."; return; }

        var fromId = FromOptions[FromIndex].Id;
        var toId   = ToOptions[ToIndex].Id;
        if (fromId == toId) { ErrorMessage = "Konto źródłowe i docelowe muszą być różne."; return; }

        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
        {
            ErrorMessage = "Podaj poprawną kwotę.";
            return;
        }

        var grosze = (long)Math.Round(Math.Abs(dec) * 100, MidpointRounding.AwayFromZero);
        if (grosze == 0) { ErrorMessage = "Kwota musi być większa od zera."; return; }

        var occurredAt = ManualEntryDateResolver.Resolve(Date, DateTimeOffset.Now);

        IsBusy = true;
        try
        {
            await _createTransfer.ExecuteAsync(
                new CreateTransferUseCase.Input(fromId, toId, new Money(grosze), occurredAt, Note), ct);
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
}
