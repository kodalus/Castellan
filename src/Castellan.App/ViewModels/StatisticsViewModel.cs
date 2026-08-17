using System.Collections.ObjectModel;
using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using Castellan.App.Resources.Styles;

namespace Castellan.App.ViewModels;

public sealed record MonthBar(
    string Label,
    string AmountDisplay,
    double BarHeight,
    Color BarColor);

public sealed record TopCatRow(
    string CategoryName,
    string AmountDisplay,
    double FillRatio);

public partial class StatisticsViewModel : ObservableObject
{
    private readonly GetMonthlyStatsUseCase _getStats;

    public ObservableCollection<MonthBar> ExpenseBars { get; } = [];
    public ObservableCollection<MonthBar> IncomeBars  { get; } = [];
    public ObservableCollection<TopCatRow> TopCategories { get; } = [];

    [ObservableProperty] private string _totalExpenseDisplay = "";
    [ObservableProperty] private string _totalIncomeDisplay  = "";
    [ObservableProperty] private string _totalNetDisplay     = "";
    [ObservableProperty] private bool   _netIsPositive       = true;
    [ObservableProperty] private bool   _hasData             = false;

    public StatisticsViewModel(GetMonthlyStatsUseCase getStats)
        => _getStats = getStats;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var stats = await _getStats.ExecuteAsync(YearMonth.Current, monthCount: 6, ct);
            const double maxBarHeight = 120.0;
            var currentMonth = YearMonth.Current;

            var maxExp = stats.Months.Max(m => m.Expense.Grosze);
            var maxInc = stats.Months.Max(m => m.Income.Grosze);

            ExpenseBars.Clear();
            IncomeBars.Clear();
            foreach (var m in stats.Months)
            {
                var isCurrent = m.Month.CompareTo(currentMonth) == 0;
                ExpenseBars.Add(new MonthBar(
                    m.MonthShort,
                    m.Expense.ToString(),
                    maxExp == 0 ? 2.0 : Math.Max(2.0, (double)m.Expense.Grosze / maxExp * maxBarHeight),
                    // Bieżący miesiąc pełnym kolorem, poprzednie przygaszone — na ciemnym
                    // tle rozjaśnianie starszych słupków (jak w motywie jasnym) dawałoby
                    // odwrotny efekt: przeszłość krzyczałaby głośniej niż teraźniejszość.
                    isCurrent ? Palette.Negative : Palette.NegativeDim));
                IncomeBars.Add(new MonthBar(
                    m.MonthShort,
                    m.Income.ToString(),
                    maxInc == 0 ? 2.0 : Math.Max(2.0, (double)m.Income.Grosze / maxInc * maxBarHeight),
                    isCurrent ? Palette.Positive : Palette.PositiveDim));
            }

            var totalExp = stats.Months.Select(m => m.Expense).Sum();
            var totalInc = stats.Months.Select(m => m.Income).Sum();
            var net      = totalInc - totalExp;

            TotalExpenseDisplay = totalExp.ToString();
            TotalIncomeDisplay  = totalInc.ToString();
            TotalNetDisplay     = net.ToString();
            NetIsPositive       = net.Grosze >= 0;

            TopCategories.Clear();
            var maxCat = stats.TopExpenseCategories.FirstOrDefault()?.TotalSpent.Grosze ?? 1L;
            foreach (var cat in stats.TopExpenseCategories)
                TopCategories.Add(new TopCatRow(
                    cat.CategoryName,
                    cat.TotalSpent.ToString(),
                    maxCat == 0 ? 0.0 : (double)cat.TotalSpent.Grosze / maxCat));

            HasData = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Statistics.Load] " + ex);
        }
    }
}
