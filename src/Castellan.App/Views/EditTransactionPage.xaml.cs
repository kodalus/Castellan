using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class EditTransactionPage : ContentPage
{
    public EditTransactionPage(EditTransactionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
