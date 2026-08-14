using Castellan.App.ViewModels;

namespace Castellan.App.Views;

public partial class BackupPage : ContentPage
{
    public BackupPage(BackupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
