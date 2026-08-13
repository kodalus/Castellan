using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class StatisticsPage : ContentPage
{
    private readonly StatisticsViewModel _vm;

    public StatisticsPage(StatisticsViewModel vm)
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
