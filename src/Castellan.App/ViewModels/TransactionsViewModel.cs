using System.Collections.ObjectModel;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record TransactionRow(
    TransactionId Id,
    string AmountDisplay,
    string DateDisplay,
    string CategoryName,
    string? Note,
    bool IsExcluded);

public partial class TransactionsViewModel : ObservableObject
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly DeleteTransactionUseCase _delete;

    public ObservableCollection<TransactionRow> Transactions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private bool _isEmpty = true;

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    public TransactionsViewModel(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        DeleteTransactionUseCase delete)
    {
        _transactions = transactions;
        _categories = categories;
        _delete = delete;
        CurrentMonth = YearMonth.Current;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Transactions.Clear();
        var txs = await _transactions.ListForMonthAsync(CurrentMonth, ct);
        var cats = await _categories.GetManyAsync(txs.Select(t => t.CategoryId).Distinct(), ct);
        var catMap = cats.ToDictionary(c => c.Id);

        foreach (var tx in txs)
        {
            var catName = catMap.TryGetValue(tx.CategoryId, out var cat) ? cat.Name : "?";
            Transactions.Add(new TransactionRow(
                tx.Id,
                tx.Amount.ToString(),
                tx.OccurredAt.ToLocalTime().ToString("d"),
                catName,
                tx.Note,
                tx.IsExcludedFromCalculations));
        }
        IsEmpty = Transactions.Count == 0;
    }

    [RelayCommand]
    private async Task PreviousMonthAsync(CancellationToken ct = default)
    {
        CurrentMonth = CurrentMonth.Previous();
        await LoadAsync(ct);
    }

    [RelayCommand]
    private async Task NextMonthAsync(CancellationToken ct = default)
    {
        CurrentMonth = CurrentMonth.Next();
        await LoadAsync(ct);
    }

    [RelayCommand]
    private static async Task AddTransactionAsync()
        => await Shell.Current.GoToAsync("addTransaction");

    [RelayCommand]
    private async Task DeleteTransactionAsync(TransactionRow row, CancellationToken ct = default)
    {
        await _delete.ExecuteAsync(row.Id, ct);
        Transactions.Remove(row);
        IsEmpty = Transactions.Count == 0;
    }
}
