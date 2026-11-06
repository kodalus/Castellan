using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class CategoryTransactionsPage : ContentPage
{
    private readonly CategoryTransactionsViewModel _vm;

    public CategoryTransactionsPage(CategoryTransactionsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    // Powrot z edycji musi odswiezyc liste i sume: poprawiona kwota albo zmieniona
    // kategoria zmienia jedno i drugie, a nieodswiezony ekran pokazywalby stan sprzed
    // wlasnej poprawki.
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }
}
