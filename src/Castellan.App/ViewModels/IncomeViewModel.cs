using System.Collections.ObjectModel;
using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(MonthParam), "month")]
public partial class IncomeViewModel : ObservableObject
{
    private readonly GetMonthOverviewUseCase _getOverview;

    private YearMonth _month = YearMonth.Current;

    [ObservableProperty] private string _monthParam = "";
    [ObservableProperty] private string _monthDisplay = "";
    [ObservableProperty] private string _plannedTotalDisplay = "—";
    [ObservableProperty] private string _actualTotalDisplay = "—";
    [ObservableProperty] private string _differenceDisplay = "";
    [ObservableProperty] private bool _isShort;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _hasNoBudget;

    public ObservableCollection<IncomeOverview> Items { get; } = [];

    public IncomeViewModel(GetMonthOverviewUseCase getOverview) => _getOverview = getOverview;

    partial void OnMonthParamChanged(string value)
    {
        if (YearMonth.TryParse(value, out var m)) _month = m;
        _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        MonthDisplay = _month.ToDisplayString();

        var data = await _getOverview.ExecuteAsync(_month, ct);

        Items.Clear();
        if (data is null)
        {
            // Brak planu na ten miesiąc — nie ma czego zestawiać.
            HasNoBudget = true;
            IsEmpty = true;
            PlannedTotalDisplay = "—";
            ActualTotalDisplay = "—";
            DifferenceDisplay = "";
            return;
        }

        HasNoBudget = false;
        foreach (var i in data.Incomes) Items.Add(i);
        IsEmpty = Items.Count == 0;

        PlannedTotalDisplay = data.TotalPlannedIncome.ToString();
        ActualTotalDisplay  = data.TotalActualIncome.ToString();

        var diff = data.TotalActualIncome - data.TotalPlannedIncome;
        IsShort = diff < Money.Zero;
        DifferenceDisplay = IsShort
            ? $"brakuje {diff.Abs()}"
            : $"ponad plan {diff}";
    }

    [RelayCommand]
    private async Task PlanIncomeAsync()
        => await Shell.Current.GoToAsync($"planEnvelopes?month={_month}");
}
