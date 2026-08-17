using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class PayDebtPage : ContentPage
{
    public PayDebtPage(PayDebtViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
