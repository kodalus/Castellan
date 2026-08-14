using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class UpdateAssetValuePage : ContentPage
{
    public UpdateAssetValuePage(UpdateAssetValueViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
