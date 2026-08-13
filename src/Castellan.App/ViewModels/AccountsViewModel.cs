using System.Collections.ObjectModel;
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
    public AccountKind Kind { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }

    public AccountRow(AccountId id, string name, string balanceDisplay, AccountKind kind, Func<Task> reconcile)
    {
        Id = id;
        Name = name;
        BalanceDisplay = balanceDisplay;
        Kind = kind;
        ReconcileCommand = new AsyncRelayCommand(reconcile);
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
            foreach (var a in list.Where(a => !a.IsArchived))
            {
                var captured = a;
                Accounts.Add(new AccountRow(
                    a.Id, a.Name, a.CurrentBalance.ToString(), a.Kind,
                    () => Shell.Current.GoToAsync($"reconcileAccount?accountId={captured.Id}&name={Uri.EscapeDataString(captured.Name)}")));
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
