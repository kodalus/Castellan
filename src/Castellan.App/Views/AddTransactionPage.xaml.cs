using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddTransactionPage : ContentPage
{
    private readonly AddTransactionViewModel _vm;

    public AddTransactionPage(AddTransactionViewModel vm)
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
