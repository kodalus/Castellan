using System.Collections.ObjectModel;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record AccountRow(AccountId Id, string Name, string BalanceDisplay, AccountKind Kind);

public partial class AccountsViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    [ObservableProperty] private bool _isEmpty = true;

    public AccountsViewModel(IAccountRepository accounts) => _accounts = accounts;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Accounts.Clear();
        var list = await _accounts.ListAsync(ct);
        foreach (var a in list)
            Accounts.Add(new AccountRow(a.Id, a.Name, a.LastReconciledBalance.ToString(), a.Kind));
        IsEmpty = Accounts.Count == 0;
    }

    [RelayCommand]
    private static async Task AddAccountAsync()
        => await Shell.Current.GoToAsync("addAccount");
}
