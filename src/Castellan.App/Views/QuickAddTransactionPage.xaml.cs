using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class QuickAddTransactionPage : ContentPage
{
    private readonly QuickAddTransactionViewModel _vm;

    public QuickAddTransactionPage(QuickAddTransactionViewModel vm)
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
