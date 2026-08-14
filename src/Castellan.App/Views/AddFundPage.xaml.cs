using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddFundPage : ContentPage
{
    public AddFundPage(AddFundViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
