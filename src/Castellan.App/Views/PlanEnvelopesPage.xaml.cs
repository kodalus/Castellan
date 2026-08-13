using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class PlanEnvelopesPage : ContentPage, IQueryAttributable
{
    private readonly PlanEnvelopesViewModel _vm;

    public PlanEnvelopesPage(PlanEnvelopesViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => _vm.ApplyQueryAttributes(query);
}
