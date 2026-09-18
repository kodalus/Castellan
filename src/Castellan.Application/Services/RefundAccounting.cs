using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Services;

/// <summary>
/// Zwrot kosztów to pieniądze wracające za coś, co już zostało kupione — reklamacja,
/// oddany towar, rozliczona zaliczka. Księgowo jest UJEMNYM WYDATKIEM, a nie przychodem:
/// ma pomniejszyć kategorię, z której poszła pierwotna płatność.
///
/// Rozpoznajemy go po samej transakcji, bez dodatkowego pola: kwota dodatnia
/// w kategorii WYDATKOWEJ nie ma innego sensownego znaczenia. Wybór kategorii jest
/// więc jednocześnie odpowiedzią na pytanie „z czego ten zwrot".
///
/// Kategorie SYSTEMOWE są z tego wyłączone, choć też są oznaczone jako wydatkowe.
/// „Nieprzypisane" to brak decyzji, a nie decyzja — wpłata, której nikt jeszcze nie
/// posortował (choćby wypłata), przepadłaby z przychodów, zanim ktokolwiek zdążyłby
/// ją zobaczyć.
/// </summary>
public static class RefundAccounting
{
    public static bool IsRefund(Transaction tx, IReadOnlyDictionary<CategoryId, Category> categories) =>
        !tx.Amount.IsNegative
        && categories.TryGetValue(tx.CategoryId, out var category)
        && !category.IsSystem
        && category.Kind == CategoryKind.Expense;

    /// <summary>
    /// Wydatek netto transakcji jako liczba dodatnia: zakup dokłada, zwrot odejmuje,
    /// a wszystko inne (przychód, transakcja w kategorii przychodowej) daje zero.
    /// </summary>
    public static long NetExpenseGrosze(Transaction tx, IReadOnlyDictionary<CategoryId, Category> categories)
    {
        if (tx.Amount.IsNegative) return -tx.Amount.Grosze;
        return IsRefund(tx, categories) ? -tx.Amount.Grosze : 0;
    }

    /// <summary>Wpływ, który naprawdę jest przychodem — czyli każdy poza zwrotem kosztów.</summary>
    public static bool IsIncome(Transaction tx, IReadOnlyDictionary<CategoryId, Category> categories) =>
        !tx.Amount.IsNegative && !IsRefund(tx, categories);
}
