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
    private const string ReserveCategoryName = "Rezerwy";

    private readonly ICategoryRepository _categories;
    private readonly IMonthBudgetRepository _budgets;
    private readonly PlanMonthUseCase _plan;
    private readonly GetFundOverviewUseCase _fundOverview;
    private readonly ITransactionRepository _transactions;
    private readonly GetAccountsWithBalancesUseCase _accountBalances;

    private YearMonth _month;
    private decimal _suggestedReserve;
    private decimal _suggestedFunds;
    private decimal _suggestedPreviousFunds;
    private decimal _suggestedBalance;
    private decimal _plannedIncomeTotal;

    [ObservableProperty] private string _monthDisplay = "";
    [ObservableProperty] private string _availableFundsText = "0";
    [ObservableProperty] private string _remainingToAllocateDisplay = "";
    [ObservableProperty] private bool _isOverAllocated;
    [ObservableProperty] private string _reserveHintDisplay = "";
    [ObservableProperty] private bool _hasReserveHint;
    [ObservableProperty] private string _incomeHintDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoIncomeHints))]
    private bool _hasIncomeHint;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoIncomeHints))]
    private string _previousIncomeHintDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoIncomeHints))]
    private bool _hasPreviousIncomeHint;

    [ObservableProperty] private string _balanceHintDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoIncomeHints))]
    private bool _hasBalanceHint;

    /// <summary>
    /// Gdy żadna podpowiedź nie ma czego pokazać, ekran przestaje cokolwiek tłumaczyć
    /// i wygląda tak, jakby podpowiedzi w ogóle nie istniały. Stąd zdanie w ich miejsce.
    /// </summary>
    public bool HasNoIncomeHints => !HasIncomeHint && !HasPreviousIncomeHint && !HasBalanceHint;
    [ObservableProperty] private string _plannedIncomeDisplay = "";
    [ObservableProperty] private bool _hasPlannedIncome;

    public ObservableCollection<EnvelopeInputRow> Envelopes { get; } = [];
    public ObservableCollection<EnvelopeInputRow> Incomes { get; } = [];

    partial void OnAvailableFundsTextChanged(string value) => Recalculate();

    private void Recalculate()
    {
        var funds = ParseAmount(AvailableFundsText);
        var planned = Envelopes.Sum(r => ParseAmount(r.PlannedAmountText));
        var remaining = funds - planned;
        IsOverAllocated = remaining < 0;
        RemainingToAllocateDisplay = $"Do zaplanowania: {remaining:N2} zł";

        var plannedIncome = Incomes.Sum(r => ParseAmount(r.PlannedAmountText));
        PlannedIncomeDisplay = $"Suma planowanych przychodów: {plannedIncome:N2} zł";
        HasPlannedIncome = plannedIncome > 0;
        _plannedIncomeTotal = plannedIncome;
    }

    /// <summary>Przepisuje sumę zaplanowanych przychodów do pola środków do dyspozycji.</summary>
    [RelayCommand]
    private void ApplyPlannedIncome() =>
        AvailableFundsText = _plannedIncomeTotal.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string text) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private async Task LoadReserveHintAsync(CancellationToken ct)
    {
        var paydateDay = Microsoft.Maui.Storage.Preferences.Get("paydate_day", 0);
        var overview   = await _fundOverview.ExecuteAsync(paydateDay, ct);

        _suggestedReserve = overview.TotalSuggestedMonthly.Grosze / 100m;
        HasReserveHint = _suggestedReserve > 0
            && Envelopes.Any(r => r.CategoryName.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase));
        ReserveHintDisplay = $"Fundusze: odkładaj {_suggestedReserve:N2} zł — wstaw do „Rezerwy”";
    }

    public PlanEnvelopesViewModel(
        ICategoryRepository categories,
        IMonthBudgetRepository budgets,
        PlanMonthUseCase plan,
        GetFundOverviewUseCase fundOverview,
        ITransactionRepository transactions,
        GetAccountsWithBalancesUseCase accountBalances)
    {
        _categories = categories;
        _budgets = budgets;
        _plan = plan;
        _fundOverview = fundOverview;
        _transactions = transactions;
        _accountBalances = accountBalances;
    }

    /// <summary>Wpisuje wpływy jako kwotę do rozdysponowania.</summary>
    [RelayCommand]
    private void ApplyIncomeHint() =>
        AvailableFundsText = _suggestedFunds.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>
    /// Obie kwoty pokazywane osobno, każda podpisana miesiącem. Wcześniej była jedna
    /// podpowiedź, która sięgała po poprzedni miesiąc tylko wtedy, gdy planowany miesiąc
    /// miał ZERO wpływów — wystarczyło, że wcześniej wpadło 800+, i ta sama kontrolka
    /// po cichu podmieniała wypłatę na kilkaset złotych.
    ///
    /// Rozdzielenie ma jeszcze jeden powód: przy wypłacie pod koniec miesiąca koperty
    /// finansuje wypłata z miesiąca POPRZEDNIEGO, więc to nie jest sytuacja awaryjna,
    /// tylko normalny sposób pracy — i musi mieć własny, stały przycisk.
    /// </summary>
    private async Task LoadIncomeHintAsync(CancellationToken ct)
    {
        var thisMonth = await SumIncomeAsync(_month, ct);
        var previous  = await SumIncomeAsync(_month.Previous(), ct);

        _suggestedFunds   = thisMonth;
        HasIncomeHint     = thisMonth > 0;
        IncomeHintDisplay = $"Wpływy w tym miesiącu: {thisMonth:N2} zł — wstaw jako środki";

        _suggestedPreviousFunds   = previous;
        HasPreviousIncomeHint     = previous > 0;
        PreviousIncomeHintDisplay = $"Wpływy z poprzedniego miesiąca: {previous:N2} zł — wstaw jako środki";
    }

    /// <summary>Dla wypłaty pod koniec miesiąca: to ona finansuje ten plan.</summary>
    [RelayCommand]
    private void ApplyPreviousIncomeHint() =>
        AvailableFundsText = _suggestedPreviousFunds.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>
    /// Jedyna podpowiedź, która jest pomiarem, a nie sumą z okresu: tyle pieniędzy
    /// realnie leży na kontach rozliczeniowych. Sama uwzględnia wydatki zrobione po
    /// wypłacie, więc działa też w miesiącu przejściowym, zanim uzbiera się bufor.
    ///
    /// Konta oszczędnościowe są pominięte celowo — tam zwykle leżą rezerwy i fundusze,
    /// czyli pieniądze, które mają już przypisane zadanie.
    /// </summary>
    [RelayCommand]
    private void ApplyBalanceHint() =>
        AvailableFundsText = _suggestedBalance.ToString("F2", CultureInfo.InvariantCulture);

    private async Task LoadBalanceHintAsync(CancellationToken ct)
    {
        var grosze = (await _accountBalances.ExecuteAsync(ct))
            .Where(a => !a.IsArchived && a.Kind == AccountKind.Checking)
            .Sum(a => a.CurrentBalance.Grosze);

        _suggestedBalance  = grosze / 100m;
        HasBalanceHint     = grosze > 0;
        BalanceHintDisplay = $"Na kontach rozliczeniowych: {_suggestedBalance:N2} zł — wstaw jako środki";
    }

    private async Task<decimal> SumIncomeAsync(YearMonth month, CancellationToken ct)
    {
        var txs = await _transactions.ListForMonthAsync(month, ct);
        // Ta sama definicja wpływu co w statystykach: dodatnia i nieodrzucona.
        var grosze = txs
            .Where(t => !t.IsExcludedFromCalculations && !t.Amount.IsNegative)
            .Sum(t => t.Amount.Grosze);
        return grosze / 100m;
    }

    /// <summary>Wpisuje sumę odpisów na fundusze do koperty „Rezerwy”.</summary>
    [RelayCommand]
    private void ApplyReserveHint()
    {
        var row = Envelopes.FirstOrDefault(r =>
            r.CategoryName.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        row.PlannedAmountText = _suggestedReserve.ToString("F2", CultureInfo.InvariantCulture);
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
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived && c.Kind == CategoryKind.Expense))
        {
            var existing = budget?.Envelopes.FirstOrDefault(e => e.CategoryId == c.Id);
            var amtText = existing is not null
                ? (existing.PlannedAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture)
                : "0";
            var row = new EnvelopeInputRow(c.Id, c.Name, amtText);
            row.PropertyChanged += (_, _) => Recalculate();
            Envelopes.Add(row);
        }

        Incomes.Clear();
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived && c.Kind == CategoryKind.Income))
        {
            var existing = budget?.IncomePlans.FirstOrDefault(p => p.CategoryId == c.Id);
            var amtText = existing is not null
                ? (existing.PlannedAmount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture)
                : "0";
            var row = new EnvelopeInputRow(c.Id, c.Name, amtText);
            row.PropertyChanged += (_, _) => Recalculate();
            Incomes.Add(row);
        }

        await LoadReserveHintAsync(ct);
        await LoadIncomeHintAsync(ct);
        await LoadBalanceHintAsync(ct);
        Recalculate();
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

        var incomeInputs = new List<PlanMonthUseCase.IncomeInput>();
        foreach (var row in Incomes)
        {
            if (!decimal.TryParse(row.PlannedAmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt)) continue;
            var grosze = (long)Math.Round(amt * 100, MidpointRounding.AwayFromZero);
            if (grosze > 0) incomeInputs.Add(new PlanMonthUseCase.IncomeInput(row.CategoryId, new Money(grosze)));
        }

        try
        {
            await _plan.ExecuteAsync(new PlanMonthUseCase.Input(_month, funds, inputs, incomeInputs), ct);
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
