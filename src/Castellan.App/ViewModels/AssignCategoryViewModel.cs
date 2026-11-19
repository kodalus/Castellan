using System.Collections.ObjectModel;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record CategoryPickerItem(CategoryId Id, string Name);

[QueryProperty(nameof(TxId), "txId")]
public partial class AssignCategoryViewModel : ObservableObject
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly AssignCategoryUseCase _assignCategory;
    private readonly Castellan.App.Services.CategoryLinkPrompt _categoryLink;

    private Money _amount = Money.Zero;
    private IReadOnlyList<Category> _allCategories = [];

    [ObservableProperty] private string _txId = "";
    [ObservableProperty] private string _merchantDisplay = "—";
    [ObservableProperty] private string _amountDisplay = "";
    [ObservableProperty] private bool _rememberRule;
    [ObservableProperty] private bool _hasMerchantKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefundHint))]
    private CategoryPickerItem? _selectedCategory;

    /// <summary>
    /// Wybór „przychód czy zwrot" ma sens tylko przy wpływie. Przy wydatku pytanie nie
    /// istnieje, a lista kategorii i tak jest jedna.
    /// </summary>
    [ObservableProperty] private bool _isInflow;

    // Wpływ to dwa różne zdarzenia i dotąd ekran ich nie rozróżniał: lista mieszała
    // kategorie przychodowe z wydatkowymi, więc zwrot za lekarza naturalnie lądował
    // w przychodowej kategorii „Zwrot kosztów" i ZAWYŻAŁ przychody, zamiast pomniejszyć
    // „Zdrowie i aptekę". Teraz tryb wybiera się wprost, a lista pokazuje tylko
    // kategorie, które w tym trybie mają sens.
    [ObservableProperty] private bool _isIncome = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefundHint))]
    private bool _isRefund;

    private bool _switchingMode;

    partial void OnIsIncomeChanged(bool value) { if (value) SelectMode(income: true,  refund: false); }
    partial void OnIsRefundChanged(bool value) { if (value) SelectMode(income: false, refund: true); }

    private void SelectMode(bool income, bool refund)
    {
        if (_switchingMode) return;
        _switchingMode = true;
        IsIncome = income;
        IsRefund = refund;
        _switchingMode = false;

        FillCategories();
    }

    /// <summary>Co się stanie po zapisaniu — tą samą kwotą, którą widać na karcie wyżej.</summary>
    public string RefundHint => !IsRefund
        ? ""
        : SelectedCategory is { } c
            ? $"Pomniejszy wydatki w kategorii „{c.Name}” o {_amount.Abs()}. Nie doda się do przychodów."
            : "Wskaż kategorię, z której odliczyć ten zwrot.";

    public ObservableCollection<CategoryPickerItem> Categories { get; } = [];

    public AssignCategoryViewModel(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        AssignCategoryUseCase assignCategory,
        Castellan.App.Services.CategoryLinkPrompt categoryLink)
    {
        _transactions = transactions;
        _categories = categories;
        _assignCategory = assignCategory;
        _categoryLink = categoryLink;
    }

    partial void OnTxIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _transactionId = new TransactionId(guid);
            _ = LoadAsync();
        }
    }

    private TransactionId? _transactionId;

    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_transactionId is not { } txId) return;

        var tx = await _transactions.GetAsync(txId, ct);
        if (tx is null) return;

        MerchantDisplay = tx.RawMerchant ?? tx.MerchantKey ?? "Nieznany sprzedawca";
        AmountDisplay = tx.Amount.ToString();
        _amount = tx.Amount;
        HasMerchantKey = tx.MerchantKey is not null || tx.RawMerchant is not null;

        _allCategories = await _categories.ListAsync(ct);
        IsInflow = !tx.Amount.IsNegative;

        // Transakcja już raz przypisana (poprawka) ma mówić, w jakim jest trybie —
        // ta sama reguła, po której zwrot rozpoznają obliczenia.
        var current = _allCategories.FirstOrDefault(c => c.Id == tx.CategoryId);
        SelectMode(
            income: !IsInflow || current is not { IsSystem: false, Kind: CategoryKind.Expense },
            refund: IsInflow && current is { IsSystem: false, Kind: CategoryKind.Expense });

        if (current is not null)
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == current.Id);
    }

    private void FillCategories()
    {
        // Wydatek nigdy nie trafia do kategorii przychodowej, a zwrot — mimo dodatniej
        // kwoty — zawsze do wydatkowej, bo właśnie ją pomniejsza.
        var kind = IsInflow && IsIncome ? CategoryKind.Income : CategoryKind.Expense;

        var previous = SelectedCategory?.Id;

        Categories.Clear();
        foreach (var c in _allCategories
                     .Where(c => !c.IsSystem && !c.IsArchived && c.Kind == kind)
                     .OrderBy(c => c.Name))
            Categories.Add(new CategoryPickerItem(c.Id, c.Name));

        SelectedCategory = previous is { } id ? Categories.FirstOrDefault(c => c.Id == id) : null;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_transactionId is not { } txId || SelectedCategory is null) return;

        await _assignCategory.ExecuteAsync(
            txId,
            SelectedCategory.Id,
            RememberRule,
            ct);

        // Rata kredytu złapana z powiadomienia trafia tutaj — bez tego pytania
        // saldo długu nie zmniejszyłoby się mimo zapłaconej raty.
        if (_amount.IsNegative)
            await _categoryLink.OfferAsync(SelectedCategory.Name, _amount.Abs(), txId, ct);

        await Shell.Current.GoToAsync("..");
    }
}
