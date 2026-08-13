using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record CategoryButton(CategoryId Id, string Name);

public partial class QuickAddTransactionViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly AddManualTransactionUseCase _addTx;

    private AccountId _defaultAccountId;
    private CategoryId _selectedCategoryId;

    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _selectedCategoryName = "";

    public ObservableCollection<CategoryButton> Categories { get; } = [];

    public QuickAddTransactionViewModel(
        IAccountRepository accounts,
        ICategoryRepository categories,
        AddManualTransactionUseCase addTx)
    {
        _accounts = accounts;
        _categories = categories;
        _addTx = addTx;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var accountList = await _accounts.ListAsync(ct);
        var def = accountList.FirstOrDefault(a => !a.IsArchived && a.Kind == AccountKind.Checking)
               ?? accountList.FirstOrDefault(a => !a.IsArchived);
        if (def is not null) _defaultAccountId = def.Id;

        Categories.Clear();
        var cats = await _categories.ListAsync(ct);
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived && c.Kind == CategoryKind.Expense))
            Categories.Add(new CategoryButton(c.Id, c.Name));

        if (Categories.Count > 0)
        {
            _selectedCategoryId = Categories[0].Id;
            SelectedCategoryName = Categories[0].Name;
        }
    }

    [RelayCommand]
    private void SelectCategory(CategoryButton cat)
    {
        _selectedCategoryId = cat.Id;
        SelectedCategoryName = cat.Name;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return;
        if (_defaultAccountId == default) return;

        var categoryId = _selectedCategoryId == default ? Category.UnsortedId : _selectedCategoryId;
        var grosze = -(long)Math.Round(dec * 100, MidpointRounding.AwayFromZero);

        try
        {
            await _addTx.ExecuteAsync(
                new AddManualTransactionUseCase.Input(_defaultAccountId, new Money(grosze), DateTimeOffset.Now, categoryId, null), ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlert("Błąd", sb.ToString(), "OK");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
