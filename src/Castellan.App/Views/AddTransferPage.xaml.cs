using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddTransferPage : ContentPage
{
    private readonly AddTransferViewModel _vm;

    public AddTransferPage(AddTransferViewModel vm)
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
