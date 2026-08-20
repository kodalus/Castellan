using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class NotificationAuditPage : ContentPage
{
    public NotificationAuditPage(NotificationAuditViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
