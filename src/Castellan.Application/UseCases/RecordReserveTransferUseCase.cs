using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Dopisuje drugą stronę odłożenia: wydatek w kategorii „Rezerwy" mówi, ile UBYŁO
/// z konta, ale nie mówi, gdzie te pieniądze wylądowały. Bez tej drugiej nogi saldo
/// konta źródłowego maleje, a docelowego nie rośnie — pieniądze po prostu ZNIKAJĄ
/// z Majątku, mimo że użytkownik nadal je ma.
///
/// Noga wchodząca jest wykluczana z budżetu jak każdy przelew (Kind = Transfer),
/// a wydatek zostaje wydatkiem w „Rezerwach" — to on obciąża kopertę. Ta sama
/// asymetria co przy przelewie potwierdzanym w skrzynce: inaczej ta sama kwota
/// wyszłaby jako przychód na koncie oszczędnościowym.
/// </summary>
public sealed class RecordReserveTransferUseCase(
    ITransactionRepository transactions,
    IAccountRepository accounts,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(
        TransactionId expenseId,
        AccountId toAccountId,
        CancellationToken ct = default)
    {
        var expense = await transactions.GetAsync(expenseId, ct);
        if (expense is null) return;

        // Przelew na to samo konto nie przenosi niczego, a zostawiłby parę wpisów
        // znoszących się nawzajem.
        if (expense.AccountId == toAccountId) return;

        var magnitude = Math.Abs(expense.Amount.Grosze);
        if (magnitude == 0) return;

        var destination = await accounts.GetAsync(toAccountId, ct);
        if (destination is null || destination.IsArchived) return;

        var incoming = Transaction.CreateManual(
            toAccountId,
            new Money(magnitude),
            expense.OccurredAt,
            Category.TransferId,
            expense.Note);

        // Własna grupa, bez pary: wydatek po drugiej stronie zostaje zwykłym wydatkiem
        // w „Rezerwach", więc nie wolno mu wpaść do grupy przelewu — tam Kind = Transfer
        // wykluczyłoby go z kopert i koperta znów pokazywałaby zero.
        incoming.SetTransferGroup(Guid.NewGuid());

        await transactions.AddAsync(incoming, ct);
        await uow.SaveChangesAsync(ct);
    }
}
