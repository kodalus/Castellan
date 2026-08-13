using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.Exceptions;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class EnvelopeInputRow(CategoryId categoryId, string categoryName, string plannedAmountText)
    : ObservableObject
{
    public CategoryId CategoryId { get; } = categoryId;
    public string CategoryName { get; } = categoryName;

    [ObservableProperty] private string _plannedAmountText = plannedAmountText;
}

public partial class PlanEnvelopesViewModel : ObservableObject, IQueryAttributable
{
    private readonly ICategoryRepository _categories;
    private readonly IMonthBudgetRepository _budgets;
    private readonly PlanMonthUseCase _plan;

    private YearMonth _month;

    [ObservableProperty] private string _monthDisplay = "";
    [ObservableProperty] private string _availableFundsText = "0";

    public ObservableCollection<EnvelopeInputRow> Envelopes { get; } = [];

    public PlanEnvelopesViewModel(
        ICategoryRepository categories,
        IMonthBudgetRepository budgets,
        PlanMonthUseCase plan)
    {
        _categories = categories;
        _budgets = budgets;
        _plan = plan;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("month", out var val) && YearMonth.TryParse(val?.ToString(), out var m))
        {
            _month = m;
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        MonthDisplay = _month.ToDisplayString();
        var budget = await _budgets.GetForMonthAsync(_month, ct);
        if (budget is not null)
            AvailableFundsText = (budget.AvailableFunds.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);

        var cats = await _categories.ListAsync(ct);
        Envelopes.Clear();
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived))
        {
            var existing = budget?.Envelopes.FirstOrDefault(e => e.CategoryId == c.Id);
            var amtText = existing is not null
                ? (existing.PlannedAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture)
                : "0";
            Envelopes.Add(new EnvelopeInputRow(c.Id, c.Name, amtText));
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (!decimal.TryParse(AvailableFundsText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var fundsDecimal)) return;
        var funds = new Money((long)Math.Round(fundsDecimal * 100, MidpointRounding.AwayFromZero));

        var inputs = new List<PlanMonthUseCase.EnvelopeInput>();
        foreach (var row in Envelopes)
        {
            if (!decimal.TryParse(row.PlannedAmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt)) continue;
            var grosze = (long)Math.Round(amt * 100, MidpointRounding.AwayFromZero);
            if (grosze > 0) inputs.Add(new PlanMonthUseCase.EnvelopeInput(row.CategoryId, new Money(grosze)));
        }

        try
        {
            await _plan.ExecuteAsync(new PlanMonthUseCase.Input(_month, funds, inputs), ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (BudgetOverAllocatedException ex)
        {
            await Shell.Current.DisplayAlertAsync(
                "Przekroczono budżet",
                $"Suma planów przekracza dostępne środki o {ex.Attempted - ex.Available}.",
                "OK");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            System.Diagnostics.Debug.WriteLine("[SavePlan] " + sb);
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Błąd zapisu planu", sb.ToString(), "OK");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
