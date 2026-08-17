using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.App.Services;
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
    private const string DefaultCategoryName = "Produkty do domu";
    private const string ReserveCategoryName = "Rezerwy";

    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly IFundRepository _funds;
    private readonly AddManualTransactionUseCase _addTx;
    private readonly ContributeToFundUseCase _contributeToFund;

    private AccountId _defaultAccountId;
    private CategoryId _selectedCategoryId;

    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _selectedCategoryName = "";

    public ObservableCollection<CategoryButton> Categories { get; } = [];

    public QuickAddTransactionViewModel(
        IAccountRepository accounts,
        ICategoryRepository categories,
        IFundRepository funds,
        AddManualTransactionUseCase addTx,
        ContributeToFundUseCase contributeToFund)
    {
        _accounts = accounts;
        _categories = categories;
        _funds = funds;
        _addTx = addTx;
        _contributeToFund = contributeToFund;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var accountList = await _accounts.ListAsync(ct);
        var active = accountList.Where(a => !a.IsArchived).ToList();

        var preferredId = DefaultAccountPreference.Get();
        var def = (preferredId is not null ? active.FirstOrDefault(a => a.Id == preferredId) : null)
               ?? active.FirstOrDefault(a => a.Kind == AccountKind.Checking)
               ?? active.FirstOrDefault();
        if (def is not null) _defaultAccountId = def.Id;

        Categories.Clear();
        var cats = await _categories.ListAsync(ct);
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived && c.Kind == CategoryKind.Expense))
            Categories.Add(new CategoryButton(c.Id, c.Name));

        var preferredCategory = Categories.FirstOrDefault(c =>
            c.Name.Equals(DefaultCategoryName, StringComparison.OrdinalIgnoreCase)) ?? Categories.FirstOrDefault();
        if (preferredCategory is not null)
        {
            _selectedCategoryId = preferredCategory.Id;
            SelectedCategoryName = preferredCategory.Name;
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
        var amountGrosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero);
        var grosze = -amountGrosze;

        try
        {
            await _addTx.ExecuteAsync(
                new AddManualTransactionUseCase.Input(_defaultAccountId, new Money(grosze), DateTimeOffset.Now, categoryId, null), ct);

            if (SelectedCategoryName.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase))
                await OfferFundContributionAsync(new Money(amountGrosze), ct);

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Błąd", sb.ToString(), "OK");
        }
    }

    /// <summary>
    /// Wydatek w kategorii "Rezerwy" to zwykle odkładanie na konkretny fundusz —
    /// pyta, który, i od razu dolicza kwotę do jego salda.
    /// </summary>
    private async Task OfferFundContributionAsync(Money amount, CancellationToken ct)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        var funds = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (funds.Count == 0) return;

        var choice = await page.DisplayActionSheet(
            "Do którego funduszu wpłacić?", "Pomiń", null, [.. funds.Select(f => f.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Pomiń") return;

        var fund = funds.FirstOrDefault(f => f.Name == choice);
        if (fund is null) return;

        await _contributeToFund.ExecuteAsync(fund.Id, amount, ct);
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
