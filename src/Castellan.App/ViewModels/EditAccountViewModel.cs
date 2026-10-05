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
    private readonly DeleteAccountUseCase _delete;
    private readonly ArchiveAccountUseCase _archive;

    [ObservableProperty] private string _accountId = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _kindIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBank))]
    private int _bankIndex;

    /// <summary>Nazwa banku wpisana ręcznie — widoczna tylko przy wyborze „Inny (wpisz)".</summary>
    [ObservableProperty] private string _customBank = "";

    public bool IsCustomBank => BankIndex == BankChoices.CustomIndex;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public List<string> KindOptions => AccountKinds.Options;
    public List<string> BankOptions => BankChoices.Options;

    private AccountId? _id;

    public EditAccountViewModel(
        IAccountRepository accounts,
        UpdateAccountUseCase update,
        DeleteAccountUseCase delete,
        ArchiveAccountUseCase archive)
    {
        _delete = delete;
        _archive = archive;
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
        KindIndex = AccountKinds.IndexOf(account.Kind);
        BankIndex = BankChoices.IndexOf(account.BankKey);
        CustomBank = BankChoices.CustomTextFor(account.BankKey) ?? "";
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
                AccountKinds.FromIndex(KindIndex),
                BankChoices.FromIndex(BankIndex, CustomBank)), ct);

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

    /// <summary>
    /// Usunięcie konta kasuje też jego historię — transakcje mają kaskadowy klucz obcy,
    /// więc znikają razem z nim, a z nimi faktyczne kwoty w kopertach tych miesięcy.
    /// Dlatego pytanie nie brzmi „na pewno?", tylko wymienia liczby i jako pierwsze
    /// wyjście proponuje archiwizację, która to samo ukrywa, nie niszcząc niczego.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        if (Shell.Current?.CurrentPage is not Page page) return;

        ErrorMessage = "";
        IsBusy = true;
        try
        {
            var impact = await _delete.PreviewAsync(id, ct);

            if (impact.IsEmpty)
            {
                var confirmEmpty = await page.DisplayAlertAsync(
                    "Usunąć konto?",
                    $"Konto „{Name}” nie ma żadnych transakcji, więc nic poza nim nie zniknie.",
                    "Usuń", "Anuluj");
                if (!confirmEmpty) return;

                await _delete.ExecuteAsync(id, ct);
                await GoBackAsync();
                return;
            }

            var partners = impact.PartnerTransactions > 0
                ? $"\n\nZ tego {impact.PartnerTransactions} to drugie nogi przelewów leżące na INNYCH kontach — "
                  + "przelew ginie parami, inaczej saldo drugiego konta zmieniłoby się bez odpowiednika."
                : "";

            var choice = await page.DisplayActionSheetAsync(
                $"Konto „{Name}” ma {impact.OwnTransactions} transakcji.{partners}",
                "Anuluj", null,
                "Zarchiwizuj (zachowaj historię)", $"Usuń konto i {impact.Total} transakcji");

            if (choice is null || choice == "Anuluj") return;

            if (choice.StartsWith("Zarchiwizuj", StringComparison.Ordinal))
            {
                await _archive.ExecuteAsync(id, ct);
                await GoBackAsync();
                return;
            }

            // Druga zgoda, bo pierwsza była wyborem z listy — a tego się nie cofnie.
            var sure = await page.DisplayAlertAsync(
                "Nie da się tego cofnąć",
                $"Zniknie {impact.Total} transakcji razem z kontem. Koperty miesięcy, w których "
                + "te wydatki leżały, pokażą inne kwoty niż dotąd.",
                "Usuń bezpowrotnie", "Anuluj");
            if (!sure) return;

            await _delete.ExecuteAsync(id, ct);
            await GoBackAsync();
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

    private static async Task GoBackAsync()
    {
        if (Shell.Current is { } shell) await shell.GoToAsync("..");
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
