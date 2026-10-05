using Castellan.App.Services;
using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class EditTransactionPage : ContentPage
{
    public EditTransactionPage(EditTransactionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        KeyboardFlow.Chain([AmountEntry, NoteEntry], vm.SaveCommand);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Przy poprawianiu transakcji prawie zawsze chodzi o kwote albo o kategorie,
        // wiec kursor staje w kwocie — a nie trzeba go tam prowadzic myszka.
        AmountEntry.FocusWhenReady();
    }
}
