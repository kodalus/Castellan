using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddDebtPage : ContentPage
{
    public AddDebtPage(AddDebtViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
