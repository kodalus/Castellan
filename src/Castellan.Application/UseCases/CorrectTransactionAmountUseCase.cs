using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Ustawia kwotę transakcji na tę odczytaną z powiadomienia. Osobny scenariusz zamiast
/// UpdateTransactionUseCase, bo tam trzeba podać wszystkie pola naraz — a tutaj mamy
/// poprawić dokładnie jedno i niczego więcej nie dotknąć.
///
/// Transakcje wykluczone z obliczeń są odrzucane: wydatek pokryty z funduszu zdjął już
/// starą kwotę z jego salda, a przelew ma drugą stronę o przeciwnej kwocie. Zmiana
/// samej kwoty rozjechałaby dane w miejscu, którego użytkownik w tym momencie nie widzi.
/// </summary>
public sealed class CorrectTransactionAmountUseCase(
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(TransactionId id, Money amount, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Transaction {id} not found.");

        if (tx.IsExcludedFromCalculations)
            throw new InvalidOperationException(
                "Tej transakcji nie można poprawić stąd — jest powiązana z funduszem, przelewem albo zastąpiona.");

        tx.SetAmount(amount);
        await uow.SaveChangesAsync(ct);
    }
}
