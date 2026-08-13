using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class ReconcileAccountPage : ContentPage, IQueryAttributable
{
    private readonly ReconcileAccountViewModel _vm;

    public ReconcileAccountPage(ReconcileAccountViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => _vm.ApplyQueryAttributes(query);
}
