using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Obniża saldo zobowiązania o zapłaconą kwotę, NIE tworząc transakcji — używane
/// wtedy, gdy wydatek już istnieje (dodany ręcznie albo złapany z powiadomienia)
/// i trzeba tylko powiązać go z konkretnym kredytem. Odpowiednik
/// ContributeToFundUseCase, tylko w drugą stronę.
///
/// Różni się tym od PayDebtInstallmentUseCase, który tworzy wydatek i obniża saldo
/// naraz — użycie tamtego tutaj zdublowałoby transakcję.
/// </summary>
public sealed class ApplyDebtPaymentUseCase(IDebtRepository debts, IUnitOfWork uow)
{
    public async Task ExecuteAsync(DebtId id, Money amount, CancellationToken ct = default)
    {
        var debt = await debts.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Debt {id} not found.");

        debt.Pay(amount);
        await uow.SaveChangesAsync(ct);
    }
}
