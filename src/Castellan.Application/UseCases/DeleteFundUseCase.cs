using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

public sealed class DeleteFundUseCase(
    IFundRepository funds,
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    /// <summary>Ile transakcji jest pokrytych z tego funduszu — do ostrzeżenia przed usunięciem.</summary>
    public async Task<int> CountLinkedTransactionsAsync(FundId id, CancellationToken ct = default)
        => (await transactions.ListPaidFromFundAsync(id, ct)).Count;

    /// <summary>
    /// Usuwa fundusz, wcześniej odpinając od niego transakcje. Transactions.PaidFromFundId
    /// nie ma klucza obcego do Funds, więc bez tego kroku zostałyby wskaźniki donikąd:
    /// takie transakcje nadal byłyby wyłączone z kopert (IsExcludedFromCalculations
    /// patrzy tylko na to, czy pole jest ustawione), ale nazwa funduszu nie dałaby się
    /// już odczytać — wydatek zniknąłby z budżetu bez żadnego widocznego powodu.
    /// Odpięcie przywraca je do kopert, co jest zgodne z tym, że fundusz przestaje istnieć.
    /// </summary>
    /// <returns>Liczba odpiętych transakcji.</returns>
    public async Task<int> ExecuteAsync(FundId id, CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Fund {id} not found.");

        var linked = await transactions.ListPaidFromFundAsync(id, ct);
        foreach (var tx in linked)
            tx.ClearFundPayment();

        await funds.RemoveAsync(fund, ct);
        await uow.SaveChangesAsync(ct);

        return linked.Count;
    }
}
