using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class CategoryRulesPage : ContentPage
{
    private readonly CategoryRulesViewModel _vm;

    public CategoryRulesPage(CategoryRulesViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }
}
