using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

public sealed class DeleteDebtUseCase(IDebtRepository debts, IUnitOfWork uow)
{
    /// <summary>
    /// Usuwa sam dług. Transakcje rat zostają nietknięte — to realne wydatki, które
    /// wydarzyły się w swoich miesiącach i skasowanie ich zafałszowałoby historię
    /// budżetu. Znika tylko ewidencja zobowiązania.
    /// </summary>
    public async Task ExecuteAsync(DebtId id, CancellationToken ct = default)
    {
        var debt = await debts.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Debt {id} not found.");

        await debts.RemoveAsync(debt, ct);
        await uow.SaveChangesAsync(ct);
    }
}
