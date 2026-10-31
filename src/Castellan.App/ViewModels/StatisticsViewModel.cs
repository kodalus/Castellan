using System.Collections.ObjectModel;
using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using Castellan.App.Resources.Styles;

namespace Castellan.App.ViewModels;

/// <summary>
/// Słupek miesiąca. Odłożone na rezerwę to OSOBNY segment nad wydanym, a nie doliczenie
/// do jego wysokości: bez tego miesiąc, w którym dużo odłożono, wyglądał identycznie jak
/// miesiąc, w którym dużo przepuszczono — a to są przeciwieństwa.
/// </summary>
public sealed record MonthBar(
    string Label,
    string AmountDisplay,
    double BarHeight,
    Color BarColor,
    double SavedHeight = 0,
    Color? SavedColor = null)
{
    public bool HasSaved => SavedHeight > 0;
}

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
    [ObservableProperty] private string _savedDisplay        = "";
    [ObservableProperty] private bool   _hasSaved            = false;
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

            // Skala liczona z sumy obu segmentów, inaczej słupek z odłożonym wyjechałby
            // poza ramkę wykresu.
            var maxExp = stats.Months.Max(m => m.Expense.Grosze + m.Saved.Grosze);
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
                    isCurrent ? Palette.Negative : Palette.NegativeDim,
                    // Odłożone w kolorze informacyjnym, nie ostrzegawczym: to nie jest strata.
                    m.Saved.Grosze <= 0 || maxExp == 0
                        ? 0
                        : Math.Max(2.0, (double)m.Saved.Grosze / maxExp * maxBarHeight),
                    isCurrent ? Palette.Info : Palette.InfoDim));
                IncomeBars.Add(new MonthBar(
                    m.MonthShort,
                    m.Income.ToString(),
                    maxInc == 0 ? 2.0 : Math.Max(2.0, (double)m.Income.Grosze / maxInc * maxBarHeight),
                    isCurrent ? Palette.Positive : Palette.PositiveDim));
            }

            var totalExp = stats.Months.Select(m => m.Expense).Sum();
            var totalInc = stats.Months.Select(m => m.Income).Sum();
            // Netto odejmuje też odłożone — ma się zgadzać z tym, co widać na kontach.
            var net      = totalInc - totalExp - stats.TotalSaved;

            TotalExpenseDisplay = totalExp.ToString();
            TotalIncomeDisplay  = totalInc.ToString();
            TotalNetDisplay     = net.ToString();
            NetIsPositive       = net.Grosze >= 0;

            HasSaved = stats.HasSaved;
            SavedDisplay = stats.TotalSaved.Grosze >= 0
                ? $"Dodatkowo odłożone na rezerwę: {stats.TotalSaved}. Netto uwzględnia tę kwotę."
                : $"Z rezerwy wyjęte więcej, niż odłożone: {new Money(-stats.TotalSaved.Grosze)}.";

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
