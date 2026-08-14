using System.Collections.ObjectModel;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
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

    [ObservableProperty] private string _txId = "";
    [ObservableProperty] private string _merchantDisplay = "—";
    [ObservableProperty] private string _amountDisplay = "";
    [ObservableProperty] private bool _rememberRule;
    [ObservableProperty] private CategoryPickerItem? _selectedCategory;
    [ObservableProperty] private bool _hasMerchantKey;

    private TransactionId? _transactionId;

    public ObservableCollection<CategoryPickerItem> Categories { get; } = [];

    public AssignCategoryViewModel(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        AssignCategoryUseCase assignCategory)
    {
        _transactions = transactions;
        _categories = categories;
        _assignCategory = assignCategory;
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

        MerchantDisplay = tx.RawMerchant ?? tx.MerchantKey ?? "Nieznany sprzedawca";
        AmountDisplay = tx.Amount.ToString();
        HasMerchantKey = tx.MerchantKey is not null || tx.RawMerchant is not null;

        var cats = await _categories.ListAsync(ct);
        Categories.Clear();
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived).OrderBy(c => c.Name))
            Categories.Add(new CategoryPickerItem(c.Id, c.Name));
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

        await Shell.Current.GoToAsync("..");
    }
}
