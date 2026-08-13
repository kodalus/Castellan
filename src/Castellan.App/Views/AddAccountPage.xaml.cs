using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddAccountPage : ContentPage
{
    public AddAccountPage(AddAccountViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
