using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record CategoryTxRow(
    TransactionId Id,
    string DateDisplay,
    string AmountDisplay,
    string Title,
    string AccountName,
    bool IsRefund,
    ICommand OpenCommand)
{
    public string RefundLabel => IsRefund ? "↩ zwrot — pomniejsza sumę" : "";
}

/// <summary>
/// Rozwinięcie jednej słupkowej liczby ze Statystyk: które to były transakcje.
///
/// Osobny ekran, a nie filtr na zakładce Transakcje, bo tamta lista ma własny miesiąc,
/// własne usuwanie i opłacanie z funduszu — a tutaj chodzi o jedno pytanie: „na co
/// poszło te 3 223,36 zł". Stuknięcie w wiersz prowadzi do edycji, więc poprawka jest
/// o jedno tapnięcie stąd.
/// </summary>
[QueryProperty(nameof(CategoryId), "categoryId")]
[QueryProperty(nameof(Month), "month")]
[QueryProperty(nameof(Months), "months")]
public partial class CategoryTransactionsViewModel : ObservableObject
{
    private readonly GetCategoryTransactionsUseCase _getTransactions;

    private Castellan.Domain.CategoryId? _id;
    private YearMonth _period = YearMonth.Current;
    private int _monthCount = 1;

    public ObservableCollection<CategoryTxRow> Items { get; } = [];

    [ObservableProperty] private string _categoryId = "";
    [ObservableProperty] private string _month = "";
    [ObservableProperty] private string _months = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _periodDisplay = "";
    [ObservableProperty] private string _totalDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    public CategoryTransactionsViewModel(GetCategoryTransactionsUseCase getTransactions)
        => _getTransactions = getTransactions;

    partial void OnCategoryIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new Castellan.Domain.CategoryId(guid);
            _ = LoadAsync();
        }
    }

    partial void OnMonthChanged(string value)
    {
        if (YearMonth.TryParse(value, out var m)) { _period = m; _ = LoadAsync(); }
    }

    partial void OnMonthsChanged(string value)
    {
        if (int.TryParse(value, out var n) && n > 0) { _monthCount = n; _ = LoadAsync(); }
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;

        var data = await _getTransactions.ExecuteAsync(id, _period, _monthCount, ct);

        Title = data.CategoryName;
        PeriodDisplay = _monthCount <= 1
            ? data.To.ToDisplayString()
            : $"{data.From.ToDisplayString()} – {data.To.ToDisplayString()}";
        TotalDisplay = data.Total.ToString();

        Items.Clear();
        foreach (var r in data.Items)
            Items.Add(new CategoryTxRow(
                r.Id,
                r.OccurredAt.ToLocalTime().ToString("dd.MM"),
                r.Amount.Abs().ToString(),
                // Nazwa sprzedawcy mówi najwięcej; notatka wtedy, gdy nazwy nie ma
                // (wpis ręczny), a na końcu zostaje sama data w tytule wiersza.
                Pick(r.Merchant, r.Note) ?? r.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy"),
                r.AccountName,
                r.IsRefund,
                new AsyncRelayCommand(() => OpenAsync(r.Id))));

        IsEmpty = Items.Count == 0;
    }

    private static string? Pick(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first
        : !string.IsNullOrWhiteSpace(second) ? second
        : null;

    [RelayCommand]
    private static async Task OpenAsync(TransactionId id)
    {
        if (Shell.Current is { } shell)
            await shell.GoToAsync($"editTransaction?txId={id.Value}");
    }
}
