using Castellan.App.Views;

namespace Castellan.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("addAccount",        typeof(AddAccountPage));
        Routing.RegisterRoute("addTransaction",    typeof(AddTransactionPage));
        Routing.RegisterRoute("planEnvelopes",     typeof(PlanEnvelopesPage));
        Routing.RegisterRoute("reconcileAccount",  typeof(ReconcileAccountPage));
        Routing.RegisterRoute("quickAdd",          typeof(QuickAddTransactionPage));
        Routing.RegisterRoute("categoryRules",     typeof(CategoryRulesPage));
        Routing.RegisterRoute("addCategoryRule",   typeof(AddCategoryRulePage));
        Routing.RegisterRoute("categories",        typeof(CategoriesPage));
        Routing.RegisterRoute("addCategory",       typeof(AddCategoryPage));
    }
}
