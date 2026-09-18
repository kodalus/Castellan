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

[QueryProperty(nameof(Amount), "amount")]
public partial class AddTransactionViewModel : ObservableObject
{
    /// <summary>
    /// Kwota podpowiedziana z nierozpoznanego powiadomienia. Tylko punkt wyjścia —
    /// znaku stamtąd nie znamy, więc wydatek albo wpływ wybiera się jak zwykle.
    /// </summary>
    public string Amount
    {
        set => AmountText = Uri.UnescapeDataString(value ?? "");
    }

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
    //
    // Tryby sa trzy, a nie dwa, bo „wplyw" kryje dwa rozne zdarzenia. Przychod to nowe
    // pieniadze (wyplata, prezent). Zwrot kosztow to pieniadze wracajace za cos juz
    // kupionego — reklamacja, oddany towar — i ma POMNIEJSZYC kategorie, z ktorej poszla
    // pierwotna platnosc, zamiast zawyzac przychody. Dlatego zwrot dostaje do wyboru
    // kategorie WYDATKOWE, choc kwota jest dodatnia.
    [ObservableProperty] private bool _isExpense = true;
    [ObservableProperty] private bool _isIncome;
    [ObservableProperty] private bool _isRefund;

    // RadioButton pilnuje wylacznosci w widoku, ale zapis czyta te pola, wiec musza byc
    // spojne takze wtedy, gdy tryb ustawia kod (np. wczytanie istniejacej transakcji).
    private bool _switchingMode;

    partial void OnIsExpenseChanged(bool value) { if (value) SelectMode(expense: true,  income: false, refund: false); }
    partial void OnIsIncomeChanged(bool value)  { if (value) SelectMode(expense: false, income: true,  refund: false); }
    partial void OnIsRefundChanged(bool value)  { if (value) SelectMode(expense: false, income: false, refund: true); }

    private void SelectMode(bool expense, bool income, bool refund)
    {
        if (_switchingMode) return;
        _switchingMode = true;
        IsExpense = expense;
        IsIncome = income;
        IsRefund = refund;
        _switchingMode = false;

        FillCategoryOptions();
    }

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
        // Zwrot idzie razem z wydatkiem: wybrana kategoria mowi, z czego ten zwrot.
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
        // trzeba za każdym razem przewijać pickera, żeby go znaleźć. Przy zwrocie
        // ta podpowiedz byla by falszywa: zwroty rozkladaja sie zupelnie inaczej.
        if (IsExpense)
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
        var grosze = IsExpense ? -magnitude : magnitude;
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
            var txId = await _addTx.ExecuteAsync(
                new AddManualTransactionUseCase.Input(accountId, new Money(grosze), occurredAt, categoryId, Note), ct);

            if (IsExpense)
                await _categoryLink.OfferAsync(categoryName, new Money(magnitude), txId, ct);

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
