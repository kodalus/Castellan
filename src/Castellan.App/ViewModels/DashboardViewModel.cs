using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;

namespace Castellan.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly GetMonthOverviewUseCase _getOverview;
    private readonly SimulateDebtPayoffUseCase _debtPayoff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private MonthOverview? _monthData;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isNotificationWarningVisible;

    [ObservableProperty] private string _debtSummaryDisplay = "";
    [ObservableProperty] private string _debtFreedomDisplay = "";
    [ObservableProperty] private bool _hasDebts;

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    public DashboardViewModel(GetMonthOverviewUseCase getOverview, SimulateDebtPayoffUseCase debtPayoff)
    {
        _getOverview = getOverview;
        _debtPayoff = debtPayoff;
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
            await LoadDebtStripAsync(ct);
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Dług na ekranie, który i tak otwierasz codziennie — bo najłatwiej unikać tego,
    /// po co trzeba specjalnie nawigować. Pasek pojawia się tylko wtedy, gdy jest co
    /// pokazać, i prowadzi do pełnego planu spłaty.
    /// </summary>
    private async Task LoadDebtStripAsync(CancellationToken ct)
    {
        var plan = await _debtPayoff.ExecuteAsync(ct: ct);

        HasDebts = plan.TotalDebt.Grosze > 0;
        if (!HasDebts)
        {
            DebtSummaryDisplay = "";
            DebtFreedomDisplay = "";
            return;
        }

        DebtSummaryDisplay = $"Długi: {plan.TotalDebt}";
        DebtFreedomDisplay = plan.FreedomDate is { } d
            ? $"wolna w {d:MM/yyyy}"
            : "podaj raty, by poznać termin";
    }

    private void CheckNotificationHealth()
    {
        // W trybie ręcznym cisza w powiadomieniach jest oczekiwana, a nie awarią —
        // ostrzeżenie byłoby stałym elementem ekranu i nauczyłoby ignorować banery.
        if (!Services.AppSettings.UsesNotifications)
        {
            IsNotificationWarningVisible = false;
            return;
        }

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

    [RelayCommand]
    private static async Task OpenDebtPlanAsync()
        => await Shell.Current.GoToAsync("debtPlan");
}
