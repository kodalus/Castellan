using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddCategoryRulePage : ContentPage
{
    private readonly AddCategoryRuleViewModel _vm;

    public AddCategoryRulePage(AddCategoryRuleViewModel vm)
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
