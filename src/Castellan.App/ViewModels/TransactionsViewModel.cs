using System.Collections.ObjectModel;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record TransactionRow(
    TransactionId Id,
    string AmountDisplay,
    string DateDisplay,
    string CategoryName,
    string? Note,
    bool IsExcluded,
    string? FundName = null,
    bool IsEditable = true,
    bool IsIncome = false,
    bool IsTransfer = false,
    bool IsProposedTransfer = false,
    /// <summary>
    /// Wpis ma druga noge, ktora zniknie razem z nim. Nie to samo co IsTransfer: noga
    /// odkladania na rezerwe zostaje zwyklym wydatkiem w kategorii „Rezerwy", zeby
    /// obciazyc koperte, a mimo to ma pare na koncie oszczednosciowym.
    /// </summary>
    bool HasTransferPair = false)
{
    public bool IsPaidFromFund => FundName is not null;
    public string FundLabel => FundName is not null ? $"⛃ z funduszu: {FundName}" : "";

    /// <summary>
    /// Noga wykrytego przelewu, na którą użytkownik jeszcze nie odpowiedział. Do tego
    /// momentu obie strony LICZĄ SIĘ do budżetu — jedna jako wydatek, druga jako
    /// przychód — bo aplikacja nie wie, czy to naprawdę przelew własny. Wiersz musi
    /// o tym mówić, inaczej wygląda jak zwykły nieprzypisany wydatek i zaprasza do
    /// nadania kategorii zamiast do rozstrzygnięcia w Skrzynce.
    /// </summary>
    public string ProposedLabel => "⇄ przelew wykryty — potwierdź w Skrzynce";
}

public partial class TransactionsViewModel : ObservableObject
{
    // Miesiac sam przechodzi na biezacy — ta zakladka zyje tak dlugo jak aplikacja.
    private readonly MonthCursor _cursor = new();

    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IFundRepository _funds;
    private readonly DeleteTransactionUseCase _delete;
    private readonly PayTransactionFromFundUseCase _payFromFund;
    private readonly ReclassifyTransferUseCase _reclassify;

    public ObservableCollection<TransactionRow> Transactions { get; } = [];

    // Pełny wczytany zestaw; `Transactions` to zawsze jego przefiltrowany widok.
    // Dwie listy, a nie jedna przeładowywana przy każdej literze — baza jest lokalna,
    // ale przeładowanie gubiłoby pozycję listy przy pisaniu.
    private readonly List<TransactionRow> _loaded = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>Lupka rozwinięta. Dopóki nie jest, ekran wygląda dokładnie jak wcześniej.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMonthBar))]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    [NotifyPropertyChangedFor(nameof(HasNoQuery))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    [NotifyPropertyChangedFor(nameof(HasNoQuery))]
    private string _query = "";

    /// <summary>
    /// Szukanie po całej historii zamiast po jednym miesiącu. Osobny przełącznik, bo
    /// pasek miesiąca musi wtedy zniknąć — inaczej ekran twierdziłby, że pokazuje
    /// listopad, pokazując wyniki z sierpnia.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMonthBar))]
    private bool _searchesEverything;

    [ObservableProperty] private string _resultSummary = "";

    public bool ShowsMonthBar => !(IsSearching && SearchesEverything);

    // Pusta lista znaczy co innego w pustym miesiącu, a co innego po nieudanym szukaniu.
    public bool HasQuery => IsSearching && !string.IsNullOrWhiteSpace(Query);
    public bool HasNoQuery => !HasQuery;

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnSearchesEverythingChanged(bool value) => _ = LoadAsync();

    /// <summary>
    /// Zwinięcie lupki kasuje też tekst i zakres. Zostawiony filtr jest gorszy niż brak
    /// filtra: lista wygląda wtedy na kompletną, a nie jest.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSearchAsync(CancellationToken ct = default)
    {
        IsSearching = !IsSearching;
        if (IsSearching) return;

        Query = "";
        if (SearchesEverything) SearchesEverything = false;  // sam przeładuje
        else await LoadAsync(ct);
    }

    [RelayCommand]
    private void ClearQuery() => Query = "";

    public TransactionsViewModel(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IFundRepository funds,
        DeleteTransactionUseCase delete,
        PayTransactionFromFundUseCase payFromFund,
        ReclassifyTransferUseCase reclassify)
    {
        _transactions = transactions;
        _categories = categories;
        _funds = funds;
        _delete = delete;
        _payFromFund = payFromFund;
        _reclassify = reclassify;
        CurrentMonth = _cursor.Month;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        CurrentMonth = _cursor.Refresh();

        _loaded.Clear();
        var txs = IsSearching && SearchesEverything
            ? await _transactions.ListAllAsync(ct)
            : await _transactions.ListForMonthAsync(CurrentMonth, ct);
        var cats = await _categories.GetManyAsync(txs.Select(t => t.CategoryId).Distinct(), ct);
        var catMap = cats.ToDictionary(c => c.Id);
        var fundMap = (await _funds.ListAsync(ct)).ToDictionary(f => f.Id, f => f.Name);

        foreach (var tx in txs)
        {
            var catName = catMap.TryGetValue(tx.CategoryId, out var cat) ? cat.Name : "?";
            var isProposed = tx.ProposedTransferGroupId.HasValue;

            // Tytulem wiersza jest to, co w nim najwazniejsze. Przy wykrytym przelewie
            // nie jest nim kategoria "Nieprzypisane" — ta nazwa mowi "nadaj mi kategorie",
            // a wlasciwa odpowiedz brzmi "rozstrzygnij w Skrzynce". Po odrzuceniu
            // propozycji wiersz wraca do swojej kategorii i da sie go sortowac normalnie.
            if (isProposed) catName = "Przelew — do potwierdzenia";
            string? fundName = tx.PaidFromFundId is { } fid && fundMap.TryGetValue(fid, out var fn) ? fn : null;
            // Transfery mają parę powiązanych wpisów, a wpisy pokryte z funduszu już
            // zdjęły kwotę z jego salda — edycja tych pól tutaj rozjechałaby dane
            // gdzie indziej, więc dla tych trzech przypadków edycja jest wyłączona.
            var isEditable = tx.Kind != TransactionKind.Transfer
                && !tx.SupersededById.HasValue
                && !tx.PaidFromFundId.HasValue;
            var local = tx.OccurredAt.ToLocalTime();
            _loaded.Add(new TransactionRow(
                tx.Id,
                tx.Amount.ToString(),
                // Jawny podział wiersza — inaczej wąska kolumna łamie rok w środku
                // liczby ("17.08.2" / "026").
                $"{local:dd.MM.}\n{local:yyyy}",
                catName,
                tx.Note,
                tx.IsExcludedFromCalculations,
                fundName,
                isEditable,
                !tx.Amount.IsNegative,
                tx.Kind == TransactionKind.Transfer,
                isProposed,
                tx.TransferGroupId.HasValue));
        }

        ApplyFilter();
    }

    /// <summary>
    /// Przepisuje wczytane wiersze do widocznej listy przez filtr. Przeszukiwane są
    /// dokładnie te pola, które w wierszu WIDAĆ — data, kategoria, notatka i kwota —
    /// żeby każdy wynik dało się wzrokowo powiązać z wpisanym tekstem. Dopasowanie
    /// opisuje <see cref="TransactionSearch"/>.
    /// </summary>
    private void ApplyFilter()
    {
        Transactions.Clear();

        foreach (var row in _loaded)
            if (TransactionSearch.Matches(Query, row.DateDisplay, row.CategoryName, row.Note, row.AmountDisplay))
                Transactions.Add(row);

        IsEmpty = Transactions.Count == 0;

        ResultSummary = !IsSearching || string.IsNullOrWhiteSpace(Query)
            ? ""
            : $"{Transactions.Count} {Wyniki(Transactions.Count)}"
              + (SearchesEverything ? " w całej historii" : $" w {CurrentMonth.ToDisplayString()}");
    }

    // Polska odmiana: 1 wynik, 2-4 wyniki, 5+ wyników — z wyjątkiem nastek (12 wyników).
    private static string Wyniki(int n)
    {
        if (n == 1) return "wynik";
        var dziesiatki = n % 100;
        var jednosci = n % 10;
        return jednosci is >= 2 and <= 4 && dziesiatki is < 12 or > 14 ? "wyniki" : "wyników";
    }

    [RelayCommand]
    private async Task PreviousMonthAsync(CancellationToken ct = default)
    {
        CurrentMonth = _cursor.Previous();
        await LoadAsync(ct);
    }

    [RelayCommand]
    private async Task NextMonthAsync(CancellationToken ct = default)
    {
        CurrentMonth = _cursor.Next();
        await LoadAsync(ct);
    }

    [RelayCommand]
    private static async Task AddTransactionAsync()
        => await Shell.Current.GoToAsync("addTransaction");

    [RelayCommand]
    private static async Task ManageRulesAsync()
        => await Shell.Current.GoToAsync("categoryRules");

    [RelayCommand]
    private static async Task AuditAmountsAsync()
        => await Shell.Current.GoToAsync("notificationAudit");

    [RelayCommand]
    private static async Task ManageCategoriesAsync()
        => await Shell.Current.GoToAsync("categories");

    [RelayCommand]
    private static async Task QuickAddAsync()
        => await Shell.Current.GoToAsync("quickAdd");

    [RelayCommand]
    private static async Task AddTransferAsync()
        => await Shell.Current.GoToAsync("addTransfer");

    [RelayCommand]
    private async Task PayFromFundAsync(TransactionRow row, CancellationToken ct = default)
    {
        var page = Shell.Current?.CurrentPage;
        if (page is null) return;

        try
        {
            if (row.IsPaidFromFund)
            {
                var undo = await page.DisplayAlertAsync(
                    "Pokryte z funduszu",
                    $"Ten wydatek jest pokryty z funduszu „{row.FundName}”. Cofnąć? Kwota wróci na saldo funduszu, a wydatek znów obciąży koperty.",
                    "Cofnij", "Zostaw");
                if (!undo) return;

                await _payFromFund.UndoAsync(row.Id, ct);
                await LoadAsync(ct);
                return;
            }

            var funds = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
            if (funds.Count == 0)
            {
                await page.DisplayAlertAsync("Brak funduszy", "Najpierw utwórz fundusz w zakładce Fundusze.", "OK");
                return;
            }

            var choice = await page.DisplayActionSheetAsync(
                "Pokryj z funduszu", "Anuluj", null, [.. funds.Select(f => f.Name)]);
            if (string.IsNullOrEmpty(choice) || choice == "Anuluj") return;

            var fund = funds.FirstOrDefault(f => f.Name == choice);
            if (fund is null) return;

            await _payFromFund.ExecuteAsync(row.Id, fund.Id, ct);
            await LoadAsync(ct);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Błąd", DescribeException(ex), "OK");
        }
    }

    private const string ActionEdit = "Edytuj";
    private const string ActionDelete = "Usuń";
    private const string ActionReclassify = "Zmień rodzaj przelewu";

    private const string KindPlain   = "Przekładanie między kontami";
    private const string KindReserve = "Odkładanie na rezerwę";
    private const string KindReturn  = "Wyjmowanie odłożonych";

    /// <summary>
    /// Pytanie „odkładasz czy przekładasz?" padało tylko raz, przy zapisie, i nie dało
    /// się go odwołać. Jedna pomyłka wypadała z koperty „Rezerwy" na zawsze, a jedynym
    /// ratunkiem było skasowanie przelewu i wpisanie go od nowa — czyli najbardziej
    /// ryzykowna operacja w aplikacji po to, żeby poprawić jedno kliknięcie.
    /// </summary>
    [RelayCommand]
    private async Task ReclassifyTransferAsync(TransactionRow row, CancellationToken ct = default)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        var choice = await page.DisplayActionSheetAsync(
            "Czym był ten przelew?", "Anuluj", null, KindPlain, KindReserve, KindReturn);

        var classification = choice switch
        {
            KindPlain   => TransferClassification.Plain,
            KindReserve => TransferClassification.Reserve,
            KindReturn  => TransferClassification.ReserveReturn,
            _           => (TransferClassification?)null,
        };
        if (classification is not { } kind) return;

        // Saldo funduszu idzie za zmianą tylko wtedy, gdy użytkownik wskaże fundusz.
        // Wskazanie jest dobrowolne: część rezerw nie ma przypisanego celu, a przymus
        // zamieniłby poprawkę w przeszkodę.
        FundId? fund = kind == TransferClassification.Plain
            ? null
            : await AskFundAsync(page, kind, ct);

        try
        {
            await _reclassify.ExecuteAsync(
                new ReclassifyTransferUseCase.Input(row.Id, kind, fund), ct);
            await LoadAsync(ct);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Nie udało się zmienić rodzaju", DescribeException(ex), "OK");
        }
    }

    /// <summary>
    /// Salda stoją przy nazwach, bo bez nich nie da się trafić w ten właściwy — nazwa
    /// sama nie mówi, ile w funduszu jeszcze jest.
    /// </summary>
    private async Task<FundId?> AskFundAsync(Page page, TransferClassification kind, CancellationToken ct)
    {
        var active = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();
        if (active.Count == 0) return null;

        var labels = active.ToDictionary(f => $"{f.Name} ({f.Balance})", f => f);

        var title = kind == TransferClassification.Reserve
            ? "Na który fundusz zapisać te pieniądze?"
            : "Z którego funduszu je wyjąć?";

        var choice = await page.DisplayActionSheetAsync(title, "Bez funduszu", null, [.. labels.Keys]);
        if (string.IsNullOrEmpty(choice) || choice == "Bez funduszu") return null;

        return labels.TryGetValue(choice, out var fund) ? fund.Id : null;
    }

    /// <summary>
    /// Te same działania, co pod przeciągnięciem wiersza, ale uruchamiane przyciskiem.
    ///
    /// Na pulpicie przeciągnięcie trzeba zrobić myszą i nikt go nie odkrywa — a tam
    /// aplikacja jest obsługiwana z klawiatury, więc działanie musi dać się wywołać
    /// Tabem i Enterem. Przeciągnięcie zostaje na telefonie, gdzie jest naturalne.
    ///
    /// Lista zależy od wiersza, bo niektóre działania byłyby na nim bez sensu: przelewu
    /// nie da się edytować pojedynczo (ma drugą nogę), a wydatek już pokryty z funduszu
    /// potrzebuje cofnięcia, nie kolejnego pokrycia.
    /// </summary>
    [RelayCommand]
    private async Task RowActionsAsync(TransactionRow row, CancellationToken ct = default)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        var fundAction = row.IsPaidFromFund ? "Cofnij pokrycie z funduszu" : "Pokryj z funduszu";

        // „Zmień rodzaj" dotyczy obu nóg przelewu, a nie tylko tej oznaczonej jako
        // przelew: noga odkładania na rezerwę zostaje zwykłym wydatkiem w „Rezerwach"
        // i to w nią najczęściej się stuka, bo to ona widnieje w kopercie.
        string[] actions = row.IsTransfer
            ? [ActionReclassify, ActionDelete]
            : row.HasTransferPair
                ? [ActionReclassify, fundAction, ActionDelete]
                : row.IsEditable
                    ? [ActionEdit, fundAction, ActionDelete]
                    : [fundAction, ActionDelete];

        var choice = await page.DisplayActionSheetAsync("Transakcja", "Anuluj", null, actions);

        if (choice == ActionReclassify) await ReclassifyTransferAsync(row, ct);
        else if (choice == ActionEdit) await EditTransactionAsync(row);
        else if (choice == fundAction) await PayFromFundAsync(row, ct);
        else if (choice == ActionDelete) await DeleteTransactionAsync(row, ct);
    }

    [RelayCommand]
    private static async Task EditTransactionAsync(TransactionRow row)
    {
        if (!row.IsEditable) return;
        await Shell.Current.GoToAsync($"editTransaction?txId={row.Id.Value}");
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(TransactionRow row, CancellationToken ct = default)
    {
        try
        {
            // Przelew ginie parami. Powiedz to przed, a nie przez zniknięcie drugiego
            // wiersza, którego użytkownik nie tykał.
            if (row.HasTransferPair && Shell.Current?.CurrentPage is Page confirmPage)
            {
                var ok = await confirmPage.DisplayAlertAsync(
                    "Usunąć przelew?",
                    "To jedna z dwóch stron przelewu między Twoimi kontami. Znikną obie — "
                    + "inaczej saldo jednego konta zmieniłoby się bez odpowiednika na drugim.",
                    "Usuń obie", "Anuluj");
                if (!ok) return;
            }

            // Przelew WYKRYTY, ale jeszcze niepotwierdzony, to inna sytuacja niż
            // potwierdzony: para może być prawdziwym przelewem albo przypadkowym
            // zestawieniem dwóch niezwiązanych kwot. Tego aplikacja nie rozstrzygnie,
            // a pomyłka w jedną stronę kasuje cudzą transakcję, w drugą — zostawia
            // sierotę, przez którą saldo nie wraca do stanu sprzed przelewu.
            var removeBoth = false;
            if (row.IsProposedTransfer)
            {
                if (Shell.Current?.CurrentPage is not Page proposedPage) return;

                const string obie = "Usuń obie strony";
                const string sama = "Usuń tylko tę";

                var wybor = await proposedPage.DisplayActionSheetAsync(
                    "To jedna z dwóch stron wykrytego przelewu — jeszcze niepotwierdzonego.",
                    "Anuluj", null, obie, sama);

                if (wybor == obie) removeBoth = true;
                else if (wybor != sama) return;
            }

            var removed = await _delete.ExecuteAsync(row.Id, removeBoth, ct);

            // Usuń WSZYSTKIE wiersze, które naprawdę zniknęły z bazy. Sam dotknięty
            // wiersz to za mało: przy przelewie druga noga zostawała na ekranie i przy
            // próbie jej usunięcia leciał wyjątek o nieistniejącej transakcji.
            foreach (var gone in removed)
            {
                var stale = Transactions.FirstOrDefault(t => t.Id == gone);
                if (stale is not null) Transactions.Remove(stale);

                // Bez tego usunięty wiersz wracałby na ekran przy zmianie tekstu
                // w lupce — filtr przepisuje listę z wczytanego zestawu.
                var stillLoaded = _loaded.FirstOrDefault(t => t.Id == gone);
                if (stillLoaded is not null) _loaded.Remove(stillLoaded);
            }

            IsEmpty = Transactions.Count == 0;
        }
        catch (Exception ex)
        {
            // Bez try/catch tutaj nieobsłużony wyjątek (np. gdy dwa szybkie przesunięcia
            // trafią w tę samą transakcję) zamykał całą aplikację zamiast pokazać komunikat.
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Błąd usuwania transakcji", DescribeException(ex), "OK");
        }
    }

    private static string DescribeException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (var e = ex; e != null; e = e.InnerException)
            sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
        return sb.ToString();
    }
}
