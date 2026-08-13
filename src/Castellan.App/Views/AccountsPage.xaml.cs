using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AccountsPage : ContentPage
{
    private readonly AccountsViewModel _vm;

    public AccountsPage(AccountsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }
}
