using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(AccountId), "accountId")]
public partial class EditAccountViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;
    private readonly UpdateAccountUseCase _update;

    [ObservableProperty] private string _accountId = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _kindIndex;
    [ObservableProperty] private int _bankIndex;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public List<string> KindOptions { get; } = ["Rachunek bieżący", "Oszczędnościowe"];
    public List<string> BankOptions => BankChoices.Options;

    private AccountId? _id;

    public EditAccountViewModel(IAccountRepository accounts, UpdateAccountUseCase update)
    {
        _accounts = accounts;
        _update = update;
    }

    partial void OnAccountIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new AccountId(guid);
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (_id is not { } id) return;
        var account = await _accounts.GetAsync(id);
        if (account is null) return;

        Name = account.Name;
        KindIndex = account.Kind == AccountKind.Savings ? 1 : 0;
        BankIndex = BankChoices.IndexOf(account.BankKey);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Podaj nazwę konta."; return; }

        IsBusy = true;
        try
        {
            await _update.ExecuteAsync(new UpdateAccountUseCase.Input(
                id,
                Name.Trim(),
                KindIndex == 1 ? AccountKind.Savings : AccountKind.Checking,
                BankChoices.FromIndex(BankIndex)), ct);

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

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
