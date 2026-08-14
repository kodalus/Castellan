using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AssignCategoryPage : ContentPage
{
    public AssignCategoryPage(AssignCategoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
