using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;

namespace Castellan.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly GetMonthOverviewUseCase _getOverview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private MonthOverview? _monthData;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isNotificationWarningVisible;

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
        try
        {
            MonthData = await _getOverview.ExecuteAsync(CurrentMonth, ct);
            CheckNotificationHealth();
        }
        finally { IsLoading = false; }
    }

    private void CheckNotificationHealth()
    {
        var ticks = Preferences.Get("last_notification_at", 0L);
        IsNotificationWarningVisible = ticks == 0 ||
            (DateTimeOffset.UtcNow - new DateTimeOffset(ticks, TimeSpan.Zero)).TotalDays > 1;
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
    private static async Task OpenStatisticsAsync()
        => await Shell.Current.GoToAsync("statistics");

    [RelayCommand]
    private async Task OpenIncomeAsync()
        => await Shell.Current.GoToAsync($"income?month={CurrentMonth}");
}
