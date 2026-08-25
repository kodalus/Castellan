using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class EditAccountPage : ContentPage
{
    public EditAccountPage(EditAccountViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
