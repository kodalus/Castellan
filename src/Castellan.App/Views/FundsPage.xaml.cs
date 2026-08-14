using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class FundsPage : ContentPage
{
    private readonly FundsViewModel _vm;

    public FundsPage(FundsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }
}
