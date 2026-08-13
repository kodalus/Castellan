using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly GetMonthOverviewUseCase _getOverview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private MonthOverview? _monthData;
    [ObservableProperty] private bool _isLoading;

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    public DashboardViewModel(GetMonthOverviewUseCase getOverview)
    {
        _getOverview = getOverview;
        CurrentMonth = YearMonth.Current;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try { MonthData = await _getOverview.ExecuteAsync(CurrentMonth, ct); }
        finally { IsLoading = false; }
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
}
