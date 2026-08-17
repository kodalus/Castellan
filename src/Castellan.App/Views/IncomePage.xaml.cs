using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class IncomePage : ContentPage
{
    private readonly IncomeViewModel _vm;

    public IncomePage(IncomeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        // Odświeża po powrocie z ekranu planowania.
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }
}
