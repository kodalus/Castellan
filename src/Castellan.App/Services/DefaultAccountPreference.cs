using Castellan.Domain;
using Microsoft.Maui.Storage;

namespace Castellan.App.Services;

/// <summary>
/// Konto domyślne przy dodawaniu transakcji (w tym błyskawicznych) — jeden
/// klucz preferencji, żeby uniknąć rozjazdu literałów między ViewModelami.
/// </summary>
public static class DefaultAccountPreference
{
    private const string Key = "default_account_id";

    public static AccountId? Get()
    {
        var raw = Preferences.Get(Key, "");
        return Guid.TryParse(raw, out var g) ? new AccountId(g) : null;
    }

    public static void Set(AccountId id) => Preferences.Set(Key, id.Value.ToString());
}
