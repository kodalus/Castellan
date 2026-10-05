using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.App.Services;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class AddTransferViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;
    private readonly IFundRepository _funds;
    private readonly CreateTransferUseCase _createTransfer;

    // Rodzaje kont docelowych, żeby dało się rozpoznać oszczędnościowe bez ponownego
    // pytania bazy w momencie zapisu.
    private readonly Dictionary<AccountId, AccountKind> _kinds = [];

    public ObservableCollection<AccountOption> FromOptions { get; } = [];
    public ObservableCollection<AccountOption> ToOptions { get; } = [];

    [ObservableProperty] private int _fromIndex = -1;
    [ObservableProperty] private int _toIndex = -1;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public AddTransferViewModel(
        IAccountRepository accounts,
        IFundRepository funds,
        CreateTransferUseCase createTransfer)
    {
        _accounts = accounts;
        _funds = funds;
        _createTransfer = createTransfer;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        FromOptions.Clear();
        ToOptions.Clear();

        _kinds.Clear();
        var list = await _accounts.ListAsync(ct);
        foreach (var a in list.Where(a => !a.IsArchived))
        {
            FromOptions.Add(new AccountOption(a.Id, a.Name));
            ToOptions.Add(new AccountOption(a.Id, a.Name));
            _kinds[a.Id] = a.Kind;
        }

        // Przelew wychodzi zwykle z konta, którego używa się na co dzień.
        var preferred = DefaultAccountPreference.Get();
        FromIndex = FromOptions.Count == 0 ? -1
            : Math.Max(0, FromOptions.ToList().FindIndex(o => preferred is not null && o.Id == preferred));

        // Domyślnie inne konto niż źródłowe, żeby formularz od razu był poprawny.
        ToIndex = ToOptions.Count < 2 ? (ToOptions.Count == 1 ? 0 : -1)
            : (FromIndex == 0 ? 1 : 0);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        ErrorMessage = "";

        if (FromIndex < 0 || FromIndex >= FromOptions.Count) { ErrorMessage = "Wybierz konto źródłowe."; return; }
        if (ToIndex < 0 || ToIndex >= ToOptions.Count)       { ErrorMessage = "Wybierz konto docelowe."; return; }

        var fromId = FromOptions[FromIndex].Id;
        var toId   = ToOptions[ToIndex].Id;
        if (fromId == toId) { ErrorMessage = "Konto źródłowe i docelowe muszą być różne."; return; }

        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
        {
            ErrorMessage = "Podaj poprawną kwotę.";
            return;
        }

        var grosze = (long)Math.Round(Math.Abs(dec) * 100, MidpointRounding.AwayFromZero);
        if (grosze == 0) { ErrorMessage = "Kwota musi być większa od zera."; return; }

        var occurredAt = ManualEntryDateResolver.Resolve(Date, DateTimeOffset.Now);

        // Przelew na własne konto oszczędnościowe kryje dwa różne zdarzenia, dla
        // aplikacji nierozróżnialne: przekładanie pieniędzy (budżet bez zmian) albo
        // odkładanie na rezerwę (budżet miesiąca ubywa). Wie to tylko użytkownik.
        var isReserve = false;
        FundId? contributeTo = null;
        FundId? withdrawFrom = null;

        var page = Shell.Current?.CurrentPage;

        if (_kinds.GetValueOrDefault(toId) == AccountKind.Savings && page is not null)
        {
            isReserve = await page.DisplayAlertAsync(
                "Odkładasz na rezerwę?",
                "Przelew na konto oszczędnościowe może być odłożeniem pieniędzy "
                + "(obciąży kopertę „Rezerwy”) albo zwykłym przekładaniem między kontami "
                + "(zniknie z budżetu).",
                "Odkładam", "Przekładam");

            if (isReserve) contributeTo = await AskForFundAsync(page, ct);
        }

        // Kierunek odwrotny: pieniądze WRACAJĄ z oszczędności do wydania. Jeśli były
        // odkładane na konkretny cel, odkładanie właśnie się kończy i saldo funduszu
        // musi zmaleć — inaczej fundusz pokazywałby zebrane pieniądze, których już nie ma.
        //
        // Pytanie zadajemy tylko wtedy, gdy pieniądze opuszczają świat oszczędności.
        // Przelew oszczędnościowe → oszczędnościowe to przełożenie w obrębie odkładania
        // i pyta wyłącznie o rezerwę, wyżej.
        else if (_kinds.GetValueOrDefault(fromId) == AccountKind.Savings
            && _kinds.GetValueOrDefault(toId) != AccountKind.Savings
            && page is not null)
        {
            var fromFund = await page.DisplayAlertAsync(
                "Wyjmujesz z funduszu?",
                "Przelew z konta oszczędnościowego może być wyjęciem odłożonych pieniędzy "
                + "(saldo funduszu zmaleje) albo zwykłym przekładaniem między kontami "
                + "(fundusze bez zmian).",
                "Z funduszu", "Przekładam");

            if (fromFund) withdrawFrom = await AskWithdrawFundAsync(page, new Money(grosze), ct);
        }

        IsBusy = true;
        try
        {
            await _createTransfer.ExecuteAsync(
                new CreateTransferUseCase.Input(
                    fromId, toId, new Money(grosze), occurredAt, Note,
                    isReserve, contributeTo, withdrawFrom), ct);
            if (Shell.Current is { } shell) await shell.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Wskazanie funduszu jest dobrowolne: część rezerw nie ma jeszcze celu, a zmuszanie
    /// do wyboru zamieniłoby pytanie w przeszkodę. Pominięcie i tak obciąża kopertę.
    /// </summary>
    private async Task<FundId?> AskForFundAsync(Page page, CancellationToken ct)
    {
        var active = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (active.Count == 0) return null;

        var choice = await page.DisplayActionSheetAsync(
            "Na który fundusz?", "Bez funduszu", null, [.. active.Select(f => f.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Bez funduszu") return null;

        return active.FirstOrDefault(f => f.Name == choice)?.Id;
    }

    /// <summary>
    /// Wybór funduszu do wyjęcia pieniędzy. Salda są pokazane przy nazwach, bo bez nich
    /// nie da się trafić w ten właściwy — nazwa sama nie mówi, ile w nim jeszcze jest.
    ///
    /// Kwota większa niż saldo wymaga potwierdzenia. Zwykle znaczy pomyłkę w funduszu
    /// albo dwukrotne zdjęcie tej samej kwoty, a saldo zeszłoby wtedy po cichu poniżej
    /// zera — liczba, której żaden ekran nie umie sensownie pokazać.
    /// </summary>
    private async Task<FundId?> AskWithdrawFundAsync(Page page, Money amount, CancellationToken ct)
    {
        var active = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (active.Count == 0) return null;

        var labels = active.ToDictionary(f => $"{f.Name} ({f.Balance})", f => f);

        var choice = await page.DisplayActionSheetAsync(
            "Z którego funduszu?", "Bez funduszu", null, [.. labels.Keys]);
        if (string.IsNullOrEmpty(choice) || choice == "Bez funduszu") return null;
        if (!labels.TryGetValue(choice, out var fund)) return null;

        if (amount.Grosze > fund.Balance.Grosze)
        {
            var confirmed = await page.DisplayAlertAsync(
                "Więcej, niż jest w funduszu",
                $"W funduszu „{fund.Name}” jest {fund.Balance}, a wyjmujesz {amount}. "
                + "Saldo funduszu zejdzie poniżej zera.",
                "Mimo to wyjmij", "Anuluj");
            if (!confirmed) return null;
        }

        return fund.Id;
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
