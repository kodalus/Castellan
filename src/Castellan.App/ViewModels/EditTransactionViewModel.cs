using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(TxId), "txId")]
public partial class EditTransactionViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly UpdateTransactionUseCase _update;

    private IReadOnlyList<Category> _allCategories = [];
    private TransactionId? _transactionId;

    // Data z pickera nie niesie godziny — jeśli użytkownik nie ruszył pola daty,
    // zapisujemy dokładny oryginalny moment zamiast go zaokrąglać do końca dnia.
    private DateTime _originalDate;
    private DateTimeOffset _originalOccurredAt;

    [ObservableProperty] private string _txId = "";
    [ObservableProperty] private bool _isLoaded;

    public ObservableCollection<AccountOption> AccountOptions { get; } = [];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];

    [ObservableProperty] private int _accountIndex = -1;
    [ObservableProperty] private int _categoryIndex = -1;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string? _note;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpense))]
    private bool _isIncome;

    public bool IsExpense
    {
        get => !IsIncome;
        set => IsIncome = !value;
    }

    partial void OnIsIncomeChanged(bool value)
    {
        if (IsLoaded) FillCategoryOptions();
    }

    public EditTransactionViewModel(
        IAccountRepository accounts,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        UpdateTransactionUseCase update)
    {
        _accounts = accounts;
        _categories = categories;
        _transactions = transactions;
        _update = update;
    }

    partial void OnTxIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _transactionId = new TransactionId(guid);
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_transactionId is not { } txId) return;

        var tx = await _transactions.GetAsync(txId, ct);
        if (tx is null) return;

        AccountOptions.Clear();
        var accountList = await _accounts.ListAsync(ct);
        foreach (var a in accountList) AccountOptions.Add(new AccountOption(a.Id, a.Name));

        _allCategories = await _categories.ListAsync(ct);

        _originalDate = tx.OccurredAt.LocalDateTime.Date;
        _originalOccurredAt = tx.OccurredAt;

        IsIncome = !tx.Amount.IsNegative;
        AmountText = (Math.Abs(tx.Amount.Grosze) / 100m).ToString("F2", CultureInfo.InvariantCulture);
        Date = _originalDate;
        Note = tx.Note;

        FillCategoryOptions();

        var accIdx = -1;
        for (var i = 0; i < AccountOptions.Count; i++)
            if (AccountOptions[i].Id == tx.AccountId) { accIdx = i; break; }
        AccountIndex = accIdx >= 0 ? accIdx : (AccountOptions.Count > 0 ? 0 : -1);

        var catIdx = -1;
        for (var i = 0; i < CategoryOptions.Count; i++)
            if (CategoryOptions[i].Id == tx.CategoryId) { catIdx = i; break; }
        CategoryIndex = catIdx >= 0 ? catIdx : (CategoryOptions.Count > 0 ? 0 : -1);

        IsLoaded = true;
    }

    private void FillCategoryOptions()
    {
        var kind = IsIncome ? CategoryKind.Income : CategoryKind.Expense;

        CategoryOptions.Clear();
        foreach (var c in _allCategories.Where(c => !c.IsSystem && !c.IsArchived && c.Kind == kind))
            CategoryOptions.Add(new CategoryOption(c.Id, c.Name));

        CategoryIndex = CategoryOptions.Count > 0 ? 0 : -1;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_transactionId is not { } txId) return;
        if (AccountIndex < 0 || AccountIndex >= AccountOptions.Count) return;
        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)) return;

        var magnitude = (long)Math.Round(Math.Abs(dec) * 100, MidpointRounding.AwayFromZero);
        if (magnitude == 0) return;
        var grosze = IsIncome ? magnitude : -magnitude;
        var accountId = AccountOptions[AccountIndex].Id;

        CategoryId categoryId;
        if (CategoryIndex >= 0 && CategoryIndex < CategoryOptions.Count)
            categoryId = CategoryOptions[CategoryIndex].Id;
        else
            categoryId = Category.UnsortedId;

        // Data nietknięta → zachowaj dokładny oryginalny moment; zmieniona →
        // policz tak samo bezpiecznie jak przy dodawaniu nowej transakcji.
        var occurredAt = Date == _originalDate
            ? _originalOccurredAt
            : ManualEntryDateResolver.Resolve(Date, DateTimeOffset.Now);

        try
        {
            await _update.ExecuteAsync(
                new UpdateTransactionUseCase.Input(txId, accountId, new Money(grosze), occurredAt, categoryId, Note), ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            System.Diagnostics.Debug.WriteLine("[SaveEditTransaction] " + sb);
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Błąd zapisu transakcji", sb.ToString(), "OK");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
