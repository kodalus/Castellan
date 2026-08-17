using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class HelpPage : ContentPage
{
    public HelpPage(HelpViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
