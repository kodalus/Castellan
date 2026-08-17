using System.Collections.ObjectModel;
using Castellan.App.Services;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed class AccountRow
{
    public AccountId Id { get; }
    public string Name { get; }
    public string BalanceDisplay { get; }
    public string KindDisplay { get; }
    public bool IsDefault { get; }
    public bool IsNotDefault => !IsDefault;
    public IAsyncRelayCommand ReconcileCommand { get; }
    public IAsyncRelayCommand SetDefaultCommand { get; }

    public AccountRow(
        AccountId id, string name, string balanceDisplay, AccountKind kind, bool isDefault,
        Func<Task> reconcile, Func<Task> setDefault)
    {
        Id = id;
        Name = name;
        BalanceDisplay = balanceDisplay;
        KindDisplay = kind switch
        {
            AccountKind.Checking => "Rachunek bieżący",
            AccountKind.Savings  => "Oszczędnościowe",
            _                    => kind.ToString(),
        };
        IsDefault = isDefault;
        ReconcileCommand = new AsyncRelayCommand(reconcile);
        SetDefaultCommand = new AsyncRelayCommand(setDefault);
    }
}

public partial class AccountsViewModel : ObservableObject
{
    private readonly GetAccountsWithBalancesUseCase _getAccounts;

    public ObservableCollection<AccountRow> Accounts { get; } = [];
    [ObservableProperty] private bool _isEmpty = true;

    public AccountsViewModel(GetAccountsWithBalancesUseCase getAccounts) => _getAccounts = getAccounts;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            Accounts.Clear();
            var list = await _getAccounts.ExecuteAsync(ct);
            var defaultId = DefaultAccountPreference.Get();
            foreach (var a in list.Where(a => !a.IsArchived))
            {
                var captured = a;
                Accounts.Add(new AccountRow(
                    a.Id, a.Name, a.CurrentBalance.ToString(), a.Kind,
                    defaultId == a.Id,
                    () => Shell.Current.GoToAsync($"reconcileAccount?accountId={captured.Id}&name={Uri.EscapeDataString(captured.Name)}"),
                    async () =>
                    {
                        DefaultAccountPreference.Set(captured.Id);
                        await LoadAsync();
                    }));
            }
            IsEmpty = Accounts.Count == 0;
        }
        catch (Exception ex)
        {
            var msg = string.Join("\n", CollectMessages(ex));
            System.Diagnostics.Debug.WriteLine("[Accounts.Load] " + msg);
            if (Shell.Current?.CurrentPage is Page p)
                await p.DisplayAlertAsync("Błąd ładowania kont", msg, "OK");
        }

        static IEnumerable<string> CollectMessages(Exception? e)
        {
            for (; e != null; e = e.InnerException)
                yield return $"[{e.GetType().Name}] {e.Message}";
        }
    }

    [RelayCommand]
    private static async Task AddAccountAsync()
        => await Shell.Current.GoToAsync("addAccount");
}
