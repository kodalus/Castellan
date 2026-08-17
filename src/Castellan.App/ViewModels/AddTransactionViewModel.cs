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

public sealed record AccountOption(AccountId Id, string Name);
public sealed record CategoryOption(CategoryId Id, string Name);

public partial class AddTransactionViewModel : ObservableObject
{
    private const string DefaultExpenseCategoryName = "Produkty do domu";

    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly AddManualTransactionUseCase _addTx;
    private readonly CategoryLinkPrompt _categoryLink;

    private IReadOnlyList<Category> _allCategories = [];

    public ObservableCollection<AccountOption> AccountOptions { get; } = [];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];

    [ObservableProperty] private int _accountIndex = -1;
    [ObservableProperty] private int _categoryIndex = -1;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string? _note;

    // Znak kwoty wynika z trybu, nie z tego, czy użytkownik pamiętał o minusie.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpense))]
    private bool _isIncome;

    // Zapisywalna, bo RadioButton "Wydatek" wiąże się TwoWay i musi móc ją ustawić.
    public bool IsExpense
    {
        get => !IsIncome;
        set => IsIncome = !value;
    }

    partial void OnIsIncomeChanged(bool value) => FillCategoryOptions();

    public AddTransactionViewModel(
        IAccountRepository accounts,
        ICategoryRepository categories,
        AddManualTransactionUseCase addTx,
        CategoryLinkPrompt categoryLink)
    {
        _accounts = accounts;
        _categories = categories;
        _addTx = addTx;
        _categoryLink = categoryLink;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        AccountOptions.Clear();

        var accounts = await _accounts.ListAsync(ct);
        foreach (var a in accounts) AccountOptions.Add(new AccountOption(a.Id, a.Name));

        _allCategories = await _categories.ListAsync(ct);
        FillCategoryOptions();

        AccountIndex = ResolveDefaultAccountIndex();
    }

    private int ResolveDefaultAccountIndex()
    {
        if (AccountOptions.Count == 0) return -1;

        var defaultId = DefaultAccountPreference.Get();
        if (defaultId is not null)
        {
            for (var i = 0; i < AccountOptions.Count; i++)
                if (AccountOptions[i].Id == defaultId) return i;
        }

        return 0;
    }

    private void FillCategoryOptions()
    {
        var kind = IsIncome ? CategoryKind.Income : CategoryKind.Expense;

        CategoryOptions.Clear();
        foreach (var c in _allCategories.Where(c => !c.IsSystem && !c.IsArchived && c.Kind == kind))
            CategoryOptions.Add(new CategoryOption(c.Id, c.Name));

        CategoryIndex = ResolveDefaultCategoryIndex();
    }

    private int ResolveDefaultCategoryIndex()
    {
        if (CategoryOptions.Count == 0) return -1;

        // Zakupy spożywcze+chemia+higiena to najczęstszy wydatek — niech nie
        // trzeba za każdym razem przewijać pickera, żeby go znaleźć.
        if (!IsIncome)
        {
            for (var i = 0; i < CategoryOptions.Count; i++)
                if (CategoryOptions[i].Name.Equals(DefaultExpenseCategoryName, StringComparison.OrdinalIgnoreCase))
                    return i;
        }

        return 0;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (AccountIndex < 0 || AccountIndex >= AccountOptions.Count) return;
        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)) return;

        // Kwotę wpisuje się zawsze dodatnią; minus dokłada tryb "Wydatek".
        var magnitude = (long)Math.Round(Math.Abs(dec) * 100, MidpointRounding.AwayFromZero);
        if (magnitude == 0) return;
        var grosze = IsIncome ? magnitude : -magnitude;
        var accountId = AccountOptions[AccountIndex].Id;

        CategoryId categoryId;
        if (CategoryIndex >= 0 && CategoryIndex < CategoryOptions.Count)
            categoryId = CategoryOptions[CategoryIndex].Id;
        else
            categoryId = Category.UnsortedId;

        var occurredAt = ManualEntryDateResolver.Resolve(Date, DateTimeOffset.Now);
        var categoryName = CategoryIndex >= 0 && CategoryIndex < CategoryOptions.Count
            ? CategoryOptions[CategoryIndex].Name
            : null;

        try
        {
            await _addTx.ExecuteAsync(
                new AddManualTransactionUseCase.Input(accountId, new Money(grosze), occurredAt, categoryId, Note), ct);

            if (IsExpense)
                await _categoryLink.OfferAsync(categoryName, new Money(magnitude), ct);

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            System.Diagnostics.Debug.WriteLine("[SaveTransaction] " + sb);
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Błąd zapisu transakcji", sb.ToString(), "OK");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
