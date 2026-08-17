using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.UseCases;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed class PayoffStepVm
{
    public string Name { get; }
    public string BalanceDisplay { get; }
    public string ClearedDisplay { get; }

    public PayoffStepVm(DebtPayoffStep step)
    {
        Name = step.Name;
        BalanceDisplay = step.Balance.ToString();
        ClearedDisplay = step.MonthCleared == 1
            ? $"za miesiąc — {step.DateCleared:MM/yyyy}"
            : $"za {step.MonthCleared} mies. — {step.DateCleared:MM/yyyy}";
    }
}

public partial class DebtPlanViewModel : ObservableObject
{
    private readonly SimulateDebtPayoffUseCase _simulate;

    private Money _minimumMonthly = Money.Zero;
    private bool _suppressRecalculate;

    [ObservableProperty] private string _totalDebtDisplay = "—";
    [ObservableProperty] private string _monthlyText = "";
    [ObservableProperty] private string _minimumHintDisplay = "";
    [ObservableProperty] private string _freedomDisplay = "";
    [ObservableProperty] private string _freedomDateDisplay = "";
    [ObservableProperty] private string _comparisonDisplay = "";
    [ObservableProperty] private bool _hasComparison;
    [ObservableProperty] private bool _isBelowMinimum;
    [ObservableProperty] private bool _hasDebts;
    [ObservableProperty] private bool _isDebtFree;

    public ObservableCollection<PayoffStepVm> Steps { get; } = [];

    public DebtPlanViewModel(SimulateDebtPayoffUseCase simulate) => _simulate = simulate;

    partial void OnMonthlyTextChanged(string value)
    {
        if (_suppressRecalculate) return;
        _ = RecalculateAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var baseline = await _simulate.ExecuteAsync(ct: ct);

        _minimumMonthly = baseline.MinimumMonthly;
        TotalDebtDisplay = baseline.TotalDebt.ToString();
        HasDebts = baseline.TotalDebt.Grosze > 0;
        IsDebtFree = !HasDebts;

        MinimumHintDisplay = $"Suma rat: {baseline.MinimumMonthly}";

        // Pole startuje na sumie rat — tyle płacisz, jeśli nic nie zmienisz.
        _suppressRecalculate = true;
        MonthlyText = (baseline.MinimumMonthly.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
        _suppressRecalculate = false;

        Apply(baseline, baseline);
    }

    private async Task RecalculateAsync()
    {
        var baseline = await _simulate.ExecuteAsync();
        var custom = await _simulate.ExecuteAsync(new Money(ParseGrosze(MonthlyText)));
        Apply(custom, baseline);
    }

    private void Apply(DebtPayoffPlan plan, DebtPayoffPlan baseline)
    {
        IsBelowMinimum = plan.BelowMinimum;

        if (!HasDebts)
        {
            FreedomDisplay = "Brak zobowiązań";
            FreedomDateDisplay = "";
            ComparisonDisplay = "";
            HasComparison = false;
            Steps.Clear();
            return;
        }

        if (plan.MonthsToFreedom is not { } months)
        {
            // Ponad 50 lat albo zerowa wpłata — konkretna data byłaby fikcją.
            FreedomDisplay = "Przy tej kwocie dług się nie kończy";
            FreedomDateDisplay = "Zwiększ miesięczną wpłatę";
            ComparisonDisplay = "";
            HasComparison = false;
            Steps.Clear();
            return;
        }

        FreedomDisplay = FormatDuration(months);
        FreedomDateDisplay = plan.FreedomDate is { } d ? $"wolna w {d:MM/yyyy}" : "";

        // Porównanie ma sens tylko wtedy, gdy realnie zmieniła się data —
        // inaczej pokazywałoby "szybciej o 0 miesięcy".
        if (baseline.MonthsToFreedom is { } baseMonths && baseMonths > months)
        {
            var saved = baseMonths - months;
            ComparisonDisplay = $"To {FormatDuration(saved)} szybciej niż przy samych ratach.";
            HasComparison = true;
        }
        else
        {
            ComparisonDisplay = "";
            HasComparison = false;
        }

        Steps.Clear();
        foreach (var s in plan.Steps) Steps.Add(new PayoffStepVm(s));
    }

    private static string FormatDuration(int months)
    {
        if (months <= 0) return "teraz";
        if (months < 12) return $"{months} mies.";

        var years = months / 12;
        var rest = months % 12;
        var yearsPart = years switch
        {
            1 => "rok",
            >= 2 and <= 4 => $"{years} lata",
            _ => $"{years} lat",
        };
        return rest == 0 ? yearsPart : $"{yearsPart} i {rest} mies.";
    }

    private static long ParseGrosze(string text)
    {
        var normalized = text.Trim().Replace(',', '.').Replace(" ", "");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0)
            return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
        return 0;
    }

    /// <summary>Przywraca kwotę do sumy rat — wyjście z "co jeśli" bez zgadywania.</summary>
    [RelayCommand]
    private void ResetToMinimum() =>
        MonthlyText = (_minimumMonthly.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
}
