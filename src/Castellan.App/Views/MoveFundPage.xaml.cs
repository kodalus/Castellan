using Castellan.App.Services;
using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class MoveFundPage : ContentPage
{
    public MoveFundPage(MoveFundViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        KeyboardFlow.Chain([AmountEntry], vm.SaveCommand);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AmountEntry.FocusWhenReady();
    }
}
