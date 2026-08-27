using System.Collections.ObjectModel;
using Castellan.Application.Repositories;
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
    AccountId AccountId = default)
{
    public bool IsPaidFromFund => FundName is not null;
    public string FundLabel => FundName is not null ? $"⛃ z funduszu: {FundName}" : "";
}

public partial class TransactionsViewModel : ObservableObject
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IFundRepository _funds;
    private readonly DeleteTransactionUseCase _delete;
    private readonly GetTransferCandidatesUseCase _transferCandidates;
    private readonly LinkAsTransferUseCase _linkAsTransfer;
    private readonly IAccountRepository _accounts;
    private readonly PayTransactionFromFundUseCase _payFromFund;

    public ObservableCollection<TransactionRow> Transactions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMonthDisplay))]
    private YearMonth _currentMonth;

    [ObservableProperty] private bool _isEmpty = true;

    public string CurrentMonthDisplay => CurrentMonth.ToDisplayString();

    public TransactionsViewModel(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IFundRepository funds,
        DeleteTransactionUseCase delete,
        GetTransferCandidatesUseCase transferCandidates,
        LinkAsTransferUseCase linkAsTransfer,
        IAccountRepository accounts,
        PayTransactionFromFundUseCase payFromFund)
    {
        _transactions = transactions;
        _categories = categories;
        _funds = funds;
        _delete = delete;
        _transferCandidates = transferCandidates;
        _linkAsTransfer = linkAsTransfer;
        _accounts = accounts;
        _payFromFund = payFromFund;
        CurrentMonth = YearMonth.Current;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Transactions.Clear();
        var txs = await _transactions.ListForMonthAsync(CurrentMonth, ct);
        var cats = await _categories.GetManyAsync(txs.Select(t => t.CategoryId).Distinct(), ct);
        var catMap = cats.ToDictionary(c => c.Id);
        var fundMap = (await _funds.ListAsync(ct)).ToDictionary(f => f.Id, f => f.Name);

        foreach (var tx in txs)
        {
            var catName = catMap.TryGetValue(tx.CategoryId, out var cat) ? cat.Name : "?";
            string? fundName = tx.PaidFromFundId is { } fid && fundMap.TryGetValue(fid, out var fn) ? fn : null;
            // Transfery mają parę powiązanych wpisów, a wpisy pokryte z funduszu już
            // zdjęły kwotę z jego salda — edycja tych pól tutaj rozjechałaby dane
            // gdzie indziej, więc dla tych trzech przypadków edycja jest wyłączona.
            var isEditable = tx.Kind != TransactionKind.Transfer
                && !tx.SupersededById.HasValue
                && !tx.PaidFromFundId.HasValue;
            var local = tx.OccurredAt.ToLocalTime();
            Transactions.Add(new TransactionRow(
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
                tx.AccountId));
        }
        IsEmpty = Transactions.Count == 0;
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

            var choice = await page.DisplayActionSheet(
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

    [RelayCommand]
    private static async Task EditTransactionAsync(TransactionRow row)
    {
        if (!row.IsEditable) return;
        await Shell.Current.GoToAsync($"editTransaction?txId={row.Id.Value}");
    }

    /// <summary>
    /// Ratunek dla przelewu, którego aplikacja nie rozpoznała. Przy przelewie między
    /// kontami tego samego banku powiadomienia nie mówią, którego konta dotyczą, więc
    /// obie nogi lądują na jednym koncie — jedna z plusem, druga z minusem. Bez tego
    /// jedynym wyjściem było skasowanie obu i wpisanie przelewu od nowa.
    /// </summary>
    [RelayCommand]
    private async Task LinkAsTransferAsync(TransactionRow row, CancellationToken ct = default)
    {
        if (Shell.Current?.CurrentPage is not Page page) return;

        try
        {
            var candidates = await _transferCandidates.ExecuteAsync(row.Id, ct);
            if (candidates.Count == 0)
            {
                await page.DisplayAlertAsync(
                    "Nie ma czego połączyć",
                    "Druga strona przelewu to wpis na dokładnie przeciwną kwotę, w ciągu dwóch dni, "
                    + "jeszcze niepołączony z żadnym przelewem. Nic takiego nie znalazłam.",
                    "OK");
                return;
            }

            var labels = candidates
                .Select(c => $"{c.OccurredAt.ToLocalTime():dd.MM HH:mm} · {c.AccountName} · {c.Amount}")
                .ToArray();

            var picked = await page.DisplayActionSheetAsync("Druga strona przelewu", "Anuluj", null, labels);
            if (string.IsNullOrEmpty(picked) || picked == "Anuluj") return;

            var index = Array.IndexOf(labels, picked);
            if (index < 0) return;
            var other = candidates[index];

            // Obie nogi na jednym koncie to właśnie objaw, przez który ta akcja istnieje.
            // Przelew łączy DWA konta, więc trzeba dopytać, dokąd naprawdę poszły pieniądze.
            AccountId? moveTo = null;
            if (other.AccountId == row.AccountId)
            {
                var targets = (await _accounts.ListAsync(ct))
                    .Where(a => !a.IsArchived && a.Id != row.AccountId)
                    .ToList();
                if (targets.Count == 0)
                {
                    await page.DisplayAlertAsync(
                        "Brak drugiego konta",
                        "Obie transakcje są na tym samym koncie, a przelew łączy dwa. Załóż konto "
                        + "docelowe i spróbuj ponownie.",
                        "OK");
                    return;
                }

                var chosen = await page.DisplayActionSheetAsync(
                    "Na które konto wpłynęły te pieniądze?", "Anuluj", null,
                    [.. targets.Select(a => a.Name)]);
                if (string.IsNullOrEmpty(chosen) || chosen == "Anuluj") return;

                moveTo = targets.FirstOrDefault(a => a.Name == chosen)?.Id;
                if (moveTo is null) return;
            }

            var result = await _linkAsTransfer.ExecuteAsync(row.Id, other.Id, moveTo, ct);
            if (result != LinkTransferResult.Linked)
            {
                await page.DisplayAlertAsync("Nie udało się połączyć", Describe(result), "OK");
                return;
            }

            await LoadAsync(ct);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Błąd łączenia w przelew", DescribeException(ex), "OK");
        }
    }

    private static string Describe(LinkTransferResult result) => result switch
    {
        LinkTransferResult.NotFound      => "Jednej z transakcji już nie ma.",
        LinkTransferResult.AlreadyLinked => "Któraś z nich należy już do przelewu.",
        LinkTransferResult.NotOpposite   => "Kwoty nie są przeciwne co do grosza.",
        LinkTransferResult.SameAccount   => "Obie strony wyszły na tym samym koncie — przelew łączy dwa.",
        _                                => "Nieznany powód.",
    };

    [RelayCommand]
    private async Task DeleteTransactionAsync(TransactionRow row, CancellationToken ct = default)
    {
        try
        {
            // Przelew ginie parami. Powiedz to przed, a nie przez zniknięcie drugiego
            // wiersza, którego użytkownik nie tykał.
            if (row.IsTransfer && Shell.Current?.CurrentPage is Page confirmPage)
            {
                var ok = await confirmPage.DisplayAlertAsync(
                    "Usunąć przelew?",
                    "To jedna z dwóch stron przelewu między Twoimi kontami. Znikną obie — "
                    + "inaczej saldo jednego konta zmieniłoby się bez odpowiednika na drugim.",
                    "Usuń obie", "Anuluj");
                if (!ok) return;
            }

            var removed = await _delete.ExecuteAsync(row.Id, ct);

            // Usuń WSZYSTKIE wiersze, które naprawdę zniknęły z bazy. Sam dotknięty
            // wiersz to za mało: przy przelewie druga noga zostawała na ekranie i przy
            // próbie jej usunięcia leciał wyjątek o nieistniejącej transakcji.
            foreach (var gone in removed)
            {
                var stale = Transactions.FirstOrDefault(t => t.Id == gone);
                if (stale is not null) Transactions.Remove(stale);
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
