using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddAssetPage : ContentPage
{
    public AddAssetPage(AddAssetViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
