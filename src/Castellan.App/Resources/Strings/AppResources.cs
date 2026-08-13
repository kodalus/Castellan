using System.Globalization;
using System.Resources;

namespace Castellan.App.Resources.Strings;

public static class AppResources
{
    private static readonly ResourceManager _rm =
        new("Castellan.App.Resources.Strings.AppResources", typeof(AppResources).Assembly);

    // Navigation
    public static string Tab_Dashboard    => Get(nameof(Tab_Dashboard));
    public static string Tab_Accounts     => Get(nameof(Tab_Accounts));
    public static string Tab_Transactions => Get(nameof(Tab_Transactions));
    public static string Tab_Envelopes    => Get(nameof(Tab_Envelopes));

    // Dashboard
    public static string Dashboard_AvailableFunds     => Get(nameof(Dashboard_AvailableFunds));
    public static string Dashboard_TotalPlanned       => Get(nameof(Dashboard_TotalPlanned));
    public static string Dashboard_RemainingToAllocate => Get(nameof(Dashboard_RemainingToAllocate));
    public static string Dashboard_NoBudget           => Get(nameof(Dashboard_NoBudget));

    // Accounts
    public static string Accounts_Empty    => Get(nameof(Accounts_Empty));
    public static string Accounts_AddTitle => Get(nameof(Accounts_AddTitle));
    public static string Accounts_Name     => Get(nameof(Accounts_Name));
    public static string Accounts_Kind     => Get(nameof(Accounts_Kind));
    public static string Accounts_Balance  => Get(nameof(Accounts_Balance));

    // Transactions
    public static string Transactions_Empty    => Get(nameof(Transactions_Empty));
    public static string Transactions_AddTitle => Get(nameof(Transactions_AddTitle));
    public static string Transactions_Account  => Get(nameof(Transactions_Account));
    public static string Transactions_Amount   => Get(nameof(Transactions_Amount));
    public static string Transactions_Date     => Get(nameof(Transactions_Date));
    public static string Transactions_Category => Get(nameof(Transactions_Category));
    public static string Transactions_Note     => Get(nameof(Transactions_Note));
    public static string Transactions_Delete   => Get(nameof(Transactions_Delete));

    // Envelopes
    public static string Envelopes_Empty          => Get(nameof(Envelopes_Empty));
    public static string Envelopes_Plan           => Get(nameof(Envelopes_Plan));
    public static string Envelopes_PlanTitle      => Get(nameof(Envelopes_PlanTitle));
    public static string Envelopes_AvailableFunds => Get(nameof(Envelopes_AvailableFunds));
    public static string Envelopes_Planned        => Get(nameof(Envelopes_Planned));
    public static string Envelopes_Actual         => Get(nameof(Envelopes_Actual));
    public static string Envelopes_Remaining      => Get(nameof(Envelopes_Remaining));

    // Common
    public static string Button_Save   => Get(nameof(Button_Save));
    public static string Button_Cancel => Get(nameof(Button_Cancel));

    private static string Get(string name) =>
        _rm.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
