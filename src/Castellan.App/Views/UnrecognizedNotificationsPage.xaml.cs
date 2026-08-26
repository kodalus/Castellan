using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class UnrecognizedNotificationsPage : ContentPage
{
    private readonly UnrecognizedNotificationsViewModel _vm;

    public UnrecognizedNotificationsPage(UnrecognizedNotificationsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
