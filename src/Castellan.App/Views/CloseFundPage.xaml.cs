using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class CloseFundPage : ContentPage
{
    public CloseFundPage(CloseFundViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
