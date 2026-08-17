using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.App.Services;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(DebtIdParam), "debtId")]
public partial class PayDebtViewModel : ObservableObject
{
    private const string DefaultCategoryName = "Kredyty i pożyczki";

    private readonly IDebtRepository _debts;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly PayDebtInstallmentUseCase _pay;

    private DebtId? _id;

    [ObservableProperty] private string _debtIdParam = "";
    [ObservableProperty] private string _debtName = "";
    [ObservableProperty] private string _balanceDisplay = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private int _accountIndex = -1;
    [ObservableProperty] private int _categoryIndex = -1;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ObservableCollection<AccountOption> AccountOptions { get; } = [];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];

    public PayDebtViewModel(
        IDebtRepository debts,
        IAccountRepository accounts,
        ICategoryRepository categories,
        PayDebtInstallmentUseCase pay)
    {
        _debts = debts;
        _accounts = accounts;
        _categories = categories;
        _pay = pay;
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

        DebtName = debt.Name;
        BalanceDisplay = $"Pozostało: {debt.Balance}";
        // Rata z umowy jako punkt wyjścia — nadpłatę wpisuje się ręcznie.
        if (debt.InstallmentAmount.Grosze > 0)
            AmountText = (debt.InstallmentAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);

        AccountOptions.Clear();
        foreach (var a in (await _accounts.ListAsync(ct)).Where(a => !a.IsArchived))
            AccountOptions.Add(new AccountOption(a.Id, a.Name));

        var preferred = DefaultAccountPreference.Get();
        AccountIndex = AccountOptions.Count == 0 ? -1
            : Math.Max(0, AccountOptions.ToList().FindIndex(o => preferred is not null && o.Id == preferred));

        CategoryOptions.Clear();
        var cats = (await _categories.ListAsync(ct))
            .Where(c => !c.IsSystem && !c.IsArchived && c.Kind == CategoryKind.Expense)
            .ToList();
        foreach (var c in cats) CategoryOptions.Add(new CategoryOption(c.Id, c.Name));

        var preferredCat = CategoryOptions.ToList().FindIndex(c =>
            c.Name.Equals(DefaultCategoryName, StringComparison.OrdinalIgnoreCase));
        CategoryIndex = CategoryOptions.Count == 0 ? -1 : Math.Max(0, preferredCat);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        if (AccountIndex < 0 || AccountIndex >= AccountOptions.Count) { ErrorMessage = "Wybierz konto."; return; }
        if (CategoryIndex < 0 || CategoryIndex >= CategoryOptions.Count) { ErrorMessage = "Wybierz kategorię."; return; }

        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
        {
            ErrorMessage = "Podaj poprawną kwotę.";
            return;
        }

        var grosze = (long)Math.Round(Math.Abs(dec) * 100, MidpointRounding.AwayFromZero);
        if (grosze == 0) { ErrorMessage = "Kwota musi być większa od zera."; return; }

        IsBusy = true;
        try
        {
            await _pay.ExecuteAsync(new PayDebtInstallmentUseCase.Input(
                id,
                AccountOptions[AccountIndex].Id,
                CategoryOptions[CategoryIndex].Id,
                new Money(grosze),
                ManualEntryDateResolver.Resolve(Date, DateTimeOffset.Now)), ct);
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
