using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AssetsPage : ContentPage
{
    private readonly AssetsViewModel _vm;

    public AssetsPage(AssetsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadCommand.Execute(null);
    }
}
