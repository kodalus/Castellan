using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddCategoryPage : ContentPage
{
    public AddCategoryPage(AddCategoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
