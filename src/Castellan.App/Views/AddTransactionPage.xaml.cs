using Castellan.App.Services;
using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class AddTransactionPage : ContentPage
{
    private readonly AddTransactionViewModel _vm;

    public AddTransactionPage(AddTransactionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        KeyboardFlow.Chain([AmountEntry, NoteEntry], vm.SaveCommand);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadCommand.ExecuteAsync(null);

        // Kwota jest pierwsza, bo to jedyna rzecz, ktora sie zawsze wie. Reszta ma
        // sensowne wartosci domyslne — konto, dzisiejsza data, czesta kategoria.
        AmountEntry.FocusWhenReady();
    }
}
