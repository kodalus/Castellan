using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class EnvelopesPage : ContentPage
{
    private readonly EnvelopesViewModel _vm;

    public EnvelopesPage(EnvelopesViewModel vm)
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
