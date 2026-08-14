using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class ContributeFundPage : ContentPage
{
    public ContributeFundPage(ContributeFundViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
