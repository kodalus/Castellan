using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>Jedna rozbieżność między zapisaną transakcją a treścią powiadomienia.</summary>
public sealed record AmountDiscrepancy(
    TransactionId TransactionId,
    DateTimeOffset OccurredAt,
    string Merchant,
    Money Stored,
    Money FromNotification,
    // Przelewy, zastapione autoryzacje i wydatki pokryte z funduszu maja powiazania
    // poza sama transakcja — zmiana kwoty jednym tapnieciem rozjechalaby saldo funduszu
    // albo druga strone przelewu. Takie wiersze pokazujemy, ale bez przycisku.
    bool IsCorrectable)
{
    public Money Difference => new(FromNotification.Grosze - Stored.Grosze);
}

public sealed record NotificationAmountAudit(
    IReadOnlyList<AmountDiscrepancy> Discrepancies,
    int CheckedCount,
    int UnreadableCount);

/// <summary>
/// Czyta zachowane treści powiadomień, parsuje je ponownie aktualnym kodem i porównuje
/// wynik z kwotą zapisanej transakcji. **Niczego nie zmienia** — to raport.
///
/// Sens: po poprawce wzorca stara transakcja zostaje z błędną kwotą, bo powstała pod
/// starym kodem. Treść powiadomienia jest jednak zachowana, więc da się sprawdzić, co
/// odczytałoby się dziś.
///
/// Transakcje poprawione ręcznie NIE pojawią się na liście: skoro kwota zgadza się już
/// z powiadomieniem, nie ma rozbieżności do zgłoszenia. Raport z założenia nie potrafi
/// więc zaproponować cofnięcia poprawnej korekty.
///
/// Odwrotnie jednak: wiersz na liście nie musi znaczyć błędu. Jeśli kwota została
/// świadomie zmieniona (rozbita płatność, częściowy zwrot), raport zobaczy różnicę
/// względem powiadomienia i ją pokaże. Dlatego to lista do przejrzenia, a nie polecenie.
/// </summary>
public sealed class AuditNotificationAmountsUseCase(
    IRawNotificationRepository rawNotifications,
    ITransactionRepository transactions,
    IEnumerable<INotificationParser> parsers)
{
    public async Task<NotificationAmountAudit> ExecuteAsync(CancellationToken ct = default)
    {
        var notifications = await rawNotifications.ListParsedAsync(ct);

        var found = new List<AmountDiscrepancy>();
        var checkedCount = 0;
        var unreadable = 0;

        foreach (var n in notifications)
        {
            if (n.TransactionId is not { } txId) continue;

            var parser = parsers.FirstOrDefault(p => p.PackageName == n.PackageName);
            if (parser is null) continue;

            var parsed = parser.TryParse(n.Title, n.Text);
            if (parsed is null)
            {
                // Treść, której dzisiejszy wzorzec nie rozumie — liczona osobno, żeby
                // „nic nie znaleziono" nie myliło się z „nie dało się sprawdzić".
                unreadable++;
                continue;
            }

            // Transakcja mogła zostać w międzyczasie usunięta.
            var tx = await transactions.GetAsync(txId, ct);
            if (tx is null) continue;

            checkedCount++;
            if (tx.Amount.Grosze == parsed.Amount.Grosze) continue;

            found.Add(new AmountDiscrepancy(
                txId,
                tx.OccurredAt,
                tx.RawMerchant ?? tx.MerchantKey ?? parsed.Merchant ?? "—",
                tx.Amount,
                parsed.Amount,
                !tx.IsExcludedFromCalculations));
        }

        return new NotificationAmountAudit(
            [.. found.OrderByDescending(d => Math.Abs(d.Difference.Grosze))],
            checkedCount,
            unreadable);
    }
}
