using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class EditDebtPage : ContentPage
{
    public EditDebtPage(EditDebtViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
