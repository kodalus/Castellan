using Castellan.App.Views;

namespace Castellan.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("addAccount",        typeof(AddAccountPage));
        Routing.RegisterRoute("addTransaction",    typeof(AddTransactionPage));
        Routing.RegisterRoute("editTransaction",   typeof(EditTransactionPage));
        Routing.RegisterRoute("addTransfer",       typeof(AddTransferPage));
        Routing.RegisterRoute("planEnvelopes",     typeof(PlanEnvelopesPage));
        Routing.RegisterRoute("reconcileAccount",  typeof(ReconcileAccountPage));
        Routing.RegisterRoute("quickAdd",          typeof(QuickAddTransactionPage));
        Routing.RegisterRoute("categoryRules",     typeof(CategoryRulesPage));
        Routing.RegisterRoute("addCategoryRule",   typeof(AddCategoryRulePage));
        Routing.RegisterRoute("categories",        typeof(CategoriesPage));
        Routing.RegisterRoute("addCategory",       typeof(AddCategoryPage));
        Routing.RegisterRoute("statistics",        typeof(StatisticsPage));
        Routing.RegisterRoute("income",            typeof(IncomePage));
        Routing.RegisterRoute("assignCategory",    typeof(AssignCategoryPage));
        Routing.RegisterRoute("addFund",            typeof(AddFundPage));
        Routing.RegisterRoute("editFund",          typeof(EditFundPage));
        Routing.RegisterRoute("contributeFund",    typeof(ContributeFundPage));
        Routing.RegisterRoute("addAsset",          typeof(AddAssetPage));
        Routing.RegisterRoute("updateAssetValue",  typeof(UpdateAssetValuePage));

        PendingNavigation.Attach(this);
    }
}
