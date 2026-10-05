using Castellan.App.Resources.Strings;
using Castellan.App.Views;

namespace Castellan.App;

public partial class AppShell : Shell
{
    /// <summary>
    /// Jeden ekran nawigacji. <paramref name="NeedsNotifications"/> oznacza ekran, który
    /// ma sens tylko tam, gdzie aplikacja czyta powiadomienia bankowe — czyli na Androidzie.
    /// </summary>
    private sealed record NavItem(
        string Title, string Icon, string Route, Type Page, bool NeedsNotifications = false);

    /// <summary>
    /// Jedna lista ekranów, dwie postacie: dolny pasek na telefonie, panel boczny na
    /// pulpicie. Kolejność jest kolejnością na pasku — a na telefonie pasek pokazuje
    /// cztery pierwsze i resztę chowa pod trzema kropkami, więc pierwsze cztery to te
    /// używane codziennie.
    /// </summary>
    private static readonly NavItem[] Screens =
    [
        new(AppResources.Tab_Dashboard,    "tab_dashboard.png",    "dashboard",    typeof(DashboardPage)),
        new(AppResources.Tab_Accounts,     "tab_accounts.png",     "accounts",     typeof(AccountsPage)),
        new(AppResources.Tab_Transactions, "tab_transactions.png", "transactions", typeof(TransactionsPage)),
        new(AppResources.Tab_Envelopes,    "tab_envelopes.png",    "envelopes",    typeof(EnvelopesPage)),
        new(AppResources.Tab_Inbox,        "tab_inbox.png",        "inbox",        typeof(InboxPage),
            NeedsNotifications: true),
        new("Fundusze",                    "tab_funds.png",        "funds",        typeof(FundsPage)),
        new("Majątek",                     "tab_assets.png",       "assets",       typeof(AssetsPage)),
        new("Kopia",                       "tab_backup.png",       "backup",       typeof(BackupPage)),
        new("Pomoc",                       "tab_help.png",         "help",         typeof(HelpPage)),
    ];

    public AppShell()
    {
        InitializeComponent();

        RegisterDetailRoutes();
        BuildNavigation();

        PendingNavigation.Attach(this);
    }

    private void BuildNavigation()
    {
        // Przechwytywanie powiadomień jest funkcją Androida. Na pulpicie Skrzynka byłaby
        // zakładką, która nigdy nic nie pokazuje, z przełącznikiem trybu pracy bez
        // żadnego skutku — tryb i tak jest tam na stałe ręczny.
#if ANDROID
        var screens = Screens;
#else
        var screens = Screens.Where(n => !n.NeedsNotifications).ToArray();
#endif

#if ANDROID
        // Ikony rysowane są jednym obrysem na siatce 24×24, żeby pasek barwił je
        // (mosiądz = wybrana) bez własnych kolorów.
        var tabs = new TabBar();
        foreach (var screen in screens) tabs.Items.Add(TabFor(screen));
        Items.Add(tabs);
#else
        // Dziewięć pozycji nie mieści się na pasku, a na pulpicie i tak nie ma po co ich
        // ściskać: panel po lewej pokazuje wszystkie naraz i nie chowa niczego pod
        // trzema kropkami. „Locked" znaczy stale otwarty — to nie jest szuflada,
        // którą się wysuwa, tylko normalna nawigacja aplikacji okienkowej.
        FlyoutBehavior = FlyoutBehavior.Locked;
        foreach (var screen in screens) Items.Add(FlyoutFor(screen));
#endif
    }

    private static Tab TabFor(NavItem screen)
    {
        var tab = new Tab
        {
            Title = screen.Title,
            Icon = ImageSource.FromFile(screen.Icon),
            Route = screen.Route,
        };
        tab.Items.Add(ContentFor(screen));
        return tab;
    }

    private static FlyoutItem FlyoutFor(NavItem screen)
    {
        var item = new FlyoutItem
        {
            Title = screen.Title,
            Icon = ImageSource.FromFile(screen.Icon),
            Route = screen.Route,
        };
        item.Items.Add(ContentFor(screen));
        return item;
    }

    private static ShellContent ContentFor(NavItem screen) =>
        new() { ContentTemplate = new DataTemplate(screen.Page) };

    private static void RegisterDetailRoutes()
    {
        Routing.RegisterRoute("addAccount",        typeof(AddAccountPage));
        Routing.RegisterRoute("editAccount",       typeof(EditAccountPage));
        Routing.RegisterRoute("addTransaction",    typeof(AddTransactionPage));
        Routing.RegisterRoute("editTransaction",   typeof(EditTransactionPage));
        Routing.RegisterRoute("addTransfer",       typeof(AddTransferPage));
        Routing.RegisterRoute("planEnvelopes",     typeof(PlanEnvelopesPage));
        Routing.RegisterRoute("notificationAudit", typeof(NotificationAuditPage));
        Routing.RegisterRoute("unrecognized",      typeof(UnrecognizedNotificationsPage));
        Routing.RegisterRoute("reconcileAccount",  typeof(ReconcileAccountPage));
        Routing.RegisterRoute("quickAdd",          typeof(QuickAddTransactionPage));
        Routing.RegisterRoute("categoryRules",     typeof(CategoryRulesPage));
        Routing.RegisterRoute("addCategoryRule",   typeof(AddCategoryRulePage));
        Routing.RegisterRoute("categories",        typeof(CategoriesPage));
        Routing.RegisterRoute("addCategory",       typeof(AddCategoryPage));
        Routing.RegisterRoute("statistics",        typeof(StatisticsPage));
        Routing.RegisterRoute("income",            typeof(IncomePage));
        Routing.RegisterRoute("assignCategory",    typeof(AssignCategoryPage));
        Routing.RegisterRoute("addFund",           typeof(AddFundPage));
        Routing.RegisterRoute("editFund",          typeof(EditFundPage));
        Routing.RegisterRoute("contributeFund",    typeof(ContributeFundPage));
        Routing.RegisterRoute("addAsset",          typeof(AddAssetPage));
        Routing.RegisterRoute("updateAssetValue",  typeof(UpdateAssetValuePage));
        Routing.RegisterRoute("addDebt",           typeof(AddDebtPage));
        Routing.RegisterRoute("editDebt",          typeof(EditDebtPage));
        Routing.RegisterRoute("payDebt",           typeof(PayDebtPage));
        Routing.RegisterRoute("debtPlan",          typeof(DebtPlanPage));
    }
}
