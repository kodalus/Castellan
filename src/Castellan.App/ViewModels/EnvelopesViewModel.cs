using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class EnvelopesViewModel : ObservableObject
{
    private readonly GetMonthOverviewUseCase _getOverview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private MonthOverview? _monthData;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private double _totalSpentRatio;
    [ObservableProperty] private string _totalActualDisplay = "";

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    public EnvelopesViewModel(GetMonthOverviewUseCase getOverview)
    {
        _getOverview = getOverview;
        CurrentMonth = YearMonth.Current;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        MonthData = await _getOverview.ExecuteAsync(CurrentMonth, ct);
        HasData = MonthData is not null;

        if (MonthData is { Envelopes.Count: > 0 })
        {
            var actualGrosze  = MonthData.Envelopes.Sum(e => Math.Abs(e.Actual.Grosze));
            var plannedGrosze = MonthData.Envelopes.Sum(e => e.Planned.Grosze);
            TotalSpentRatio   = plannedGrosze == 0 ? 0.0
                : Math.Clamp((double)actualGrosze / plannedGrosze, 0.0, 1.0);
            TotalActualDisplay = new Money(actualGrosze).ToString();
        }
        else
        {
            TotalSpentRatio    = 0.0;
            TotalActualDisplay = Money.Zero.ToString();
        }
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
    private async Task PlanMonthAsync()
        => await Shell.Current.GoToAsync($"planEnvelopes?month={CurrentMonth}");
}
