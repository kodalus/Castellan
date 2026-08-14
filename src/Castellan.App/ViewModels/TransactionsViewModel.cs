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
    bool IsExcluded,
    string? FundName = null,
    bool IsEditable = true)
{
    public bool IsPaidFromFund => FundName is not null;
    public string FundLabel => FundName is not null ? $"⛃ z funduszu: {FundName}" : "";
}

public partial class TransactionsViewModel : ObservableObject
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IFundRepository _funds;
    private readonly DeleteTransactionUseCase _delete;
    private readonly PayTransactionFromFundUseCase _payFromFund;

    public ObservableCollection<TransactionRow> Transactions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private bool _isEmpty = true;

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    public TransactionsViewModel(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IFundRepository funds,
        DeleteTransactionUseCase delete,
        PayTransactionFromFundUseCase payFromFund)
    {
        _transactions = transactions;
        _categories = categories;
        _funds = funds;
        _delete = delete;
        _payFromFund = payFromFund;
        CurrentMonth = YearMonth.Current;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Transactions.Clear();
        var txs = await _transactions.ListForMonthAsync(CurrentMonth, ct);
        var cats = await _categories.GetManyAsync(txs.Select(t => t.CategoryId).Distinct(), ct);
        var catMap = cats.ToDictionary(c => c.Id);
        var fundMap = (await _funds.ListAsync(ct)).ToDictionary(f => f.Id, f => f.Name);

        foreach (var tx in txs)
        {
            var catName = catMap.TryGetValue(tx.CategoryId, out var cat) ? cat.Name : "?";
            string? fundName = tx.PaidFromFundId is { } fid && fundMap.TryGetValue(fid, out var fn) ? fn : null;
            // Transfery mają parę powiązanych wpisów, a wpisy pokryte z funduszu już
            // zdjęły kwotę z jego salda — edycja tych pól tutaj rozjechałaby dane
            // gdzie indziej, więc dla tych trzech przypadków edycja jest wyłączona.
            var isEditable = tx.Kind != TransactionKind.Transfer
                && !tx.SupersededById.HasValue
                && !tx.PaidFromFundId.HasValue;
            Transactions.Add(new TransactionRow(
                tx.Id,
                tx.Amount.ToString(),
                tx.OccurredAt.ToLocalTime().ToString("d"),
                catName,
                tx.Note,
                tx.IsExcludedFromCalculations,
                fundName,
                isEditable));
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
    private static async Task ManageRulesAsync()
        => await Shell.Current.GoToAsync("categoryRules");

    [RelayCommand]
    private static async Task ManageCategoriesAsync()
        => await Shell.Current.GoToAsync("categories");

    [RelayCommand]
    private static async Task QuickAddAsync()
        => await Shell.Current.GoToAsync("quickAdd");

    [RelayCommand]
    private async Task PayFromFundAsync(TransactionRow row, CancellationToken ct = default)
    {
        var page = Shell.Current?.CurrentPage;
        if (page is null) return;

        if (row.IsPaidFromFund)
        {
            var undo = await page.DisplayAlertAsync(
                "Pokryte z funduszu",
                $"Ten wydatek jest pokryty z funduszu „{row.FundName}”. Cofnąć? Kwota wróci na saldo funduszu, a wydatek znów obciąży koperty.",
                "Cofnij", "Zostaw");
            if (!undo) return;

            await _payFromFund.UndoAsync(row.Id, ct);
            await LoadAsync(ct);
            return;
        }

        var funds = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (funds.Count == 0)
        {
            await page.DisplayAlertAsync("Brak funduszy", "Najpierw utwórz fundusz w zakładce Fundusze.", "OK");
            return;
        }

        var choice = await page.DisplayActionSheet(
            "Pokryj z funduszu", "Anuluj", null, [.. funds.Select(f => f.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Anuluj") return;

        var fund = funds.FirstOrDefault(f => f.Name == choice);
        if (fund is null) return;

        await _payFromFund.ExecuteAsync(row.Id, fund.Id, ct);
        await LoadAsync(ct);
    }

    [RelayCommand]
    private static async Task EditTransactionAsync(TransactionRow row)
    {
        if (!row.IsEditable) return;
        await Shell.Current.GoToAsync($"editTransaction?txId={row.Id.Value}");
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(TransactionRow row, CancellationToken ct = default)
    {
        await _delete.ExecuteAsync(row.Id, ct);
        Transactions.Remove(row);
        IsEmpty = Transactions.Count == 0;
    }
}
