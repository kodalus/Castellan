using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class InboxPage : ContentPage
{
    private readonly InboxViewModel _vm;

    public InboxPage(InboxViewModel vm)
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
