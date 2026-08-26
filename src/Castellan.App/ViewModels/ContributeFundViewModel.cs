using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.App.Services;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

/// <summary>Konto do wyboru w pytaniu „skąd" i „dokąd".</summary>
public sealed record AccountChoice(AccountId Id, string Name);

[QueryProperty(nameof(FundId), "fundId")]
public partial class ContributeFundViewModel : ObservableObject
{
    private readonly IFundRepository _funds;
    private readonly IAccountRepository _accounts;
    private readonly ContributeToFundUseCase _contribute;
    private readonly CreateTransferUseCase _createTransfer;

    [ObservableProperty] private string _fundId = "";
    [ObservableProperty] private string _fundName = "—";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Domyślnie wyłączony. Odłożenie pieniędzy zwykle ma odpowiednik w prawdziwym
    /// przelewie, a ten przyjdzie osobno z powiadomienia i sam obciąży kopertę „Rezerwy".
    /// Zapisanie obu policzyłoby tę samą kwotę dwa razy.
    /// </summary>
    [ObservableProperty] private bool _recordTransfer;

    /// <summary>
    /// Dwa pytania zamiast jednego „z jakiego konta". Sam wydatek mówił tylko, ile UBYŁO
    /// z konta źródłowego — bez konta docelowego saldo jednego malało, drugiego nie rosło,
    /// a pieniądze znikały z Majątku, mimo że nadal były. Gotówka też jest kontem, więc
    /// „odłożone do szuflady" ma tu swoją pozycję i nie potrzebuje wyjątku.
    /// </summary>
    public ObservableCollection<AccountChoice> Accounts { get; } = [];

    [ObservableProperty] private AccountChoice? _fromAccount;
    [ObservableProperty] private AccountChoice? _toAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private FundId? _id;

    public ContributeFundViewModel(
        IFundRepository funds,
        IAccountRepository accounts,
        ContributeToFundUseCase contribute,
        CreateTransferUseCase createTransfer)
    {
        _funds = funds;
        _accounts = accounts;
        _contribute = contribute;
        _createTransfer = createTransfer;
        _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        Accounts.Clear();
        foreach (var a in (await _accounts.ListAsync()).Where(a => !a.IsArchived))
            Accounts.Add(new AccountChoice(a.Id, a.Name));

        if (Accounts.Count == 0) return;

        // Zgadujemy najczęstszy przypadek: z konta codziennego na oszczędnościowe.
        // Zgadujemy tylko wstępnie — pola i tak są widoczne dopiero po włączeniu zapisu.
        var preferred = DefaultAccountPreference.Get();
        FromAccount = Accounts.FirstOrDefault(a => preferred is not null && a.Id == preferred)
                      ?? Accounts[0];

        var savings = (await _accounts.ListAsync())
            .FirstOrDefault(a => !a.IsArchived && a.Kind == AccountKind.Savings);
        ToAccount = savings is not null
            ? Accounts.FirstOrDefault(a => a.Id == savings.Id)
            : Accounts.FirstOrDefault(a => a.Id != FromAccount?.Id);
    }

    partial void OnFundIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new FundId(guid);
            _ = LoadNameAsync();
        }
    }

    private async Task LoadNameAsync()
    {
        if (_id is not { } id) return;
        var fund = await _funds.GetAsync(id);
        if (fund is not null) FundName = fund.Name;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        var grosze = ParseGrosze(AmountText);
        if (grosze <= 0) { ErrorMessage = "Podaj poprawną kwotę."; return; }

        if (RecordTransfer)
        {
            if (FromAccount is null || ToAccount is null)
            {
                ErrorMessage = "Wybierz konto źródłowe i docelowe.";
                return;
            }

            if (FromAccount.Id == ToAccount.Id)
            {
                ErrorMessage = "Konto źródłowe i docelowe muszą być różne.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            if (RecordTransfer)
            {
                // Ta sama droga co przy przelewie z zakładki Transakcje: noga wychodząca
                // obciąża kopertę „Rezerwy", wchodząca wypada z budżetu, a saldo funduszu
                // rośnie. Jeden mechanizm zamiast dwóch, które robią to samo inaczej.
                await _createTransfer.ExecuteAsync(new CreateTransferUseCase.Input(
                    FromAccount!.Id, ToAccount!.Id, new Money(grosze), DateTimeOffset.Now,
                    Note: $"Wpłata na fundusz: {FundName}", IsReserve: true, ContributeTo: id), ct);
            }
            else
            {
                await _contribute.ExecuteAsync(id, new Money(grosze), ct);
            }

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            ErrorMessage = sb.ToString();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static long ParseGrosze(string text)
    {
        var normalized = text.Trim().Replace(',', '.').Replace(" ", "");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0)
            return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
        return 0;
    }
}
