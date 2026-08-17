using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class EditFundPage : ContentPage
{
    public EditFundPage(EditFundViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
