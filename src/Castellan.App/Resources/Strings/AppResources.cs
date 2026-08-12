using System.Globalization;
using System.Resources;

namespace Castellan.App.Resources.Strings;

public static class AppResources
{
    private static readonly ResourceManager _rm =
        new("Castellan.App.Resources.Strings.AppResources", typeof(AppResources).Assembly);

    public static string Tab_Dashboard    => Get(nameof(Tab_Dashboard));
    public static string Tab_Accounts     => Get(nameof(Tab_Accounts));
    public static string Tab_Transactions => Get(nameof(Tab_Transactions));
    public static string Tab_Envelopes    => Get(nameof(Tab_Envelopes));

    private static string Get(string name) =>
        _rm.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
