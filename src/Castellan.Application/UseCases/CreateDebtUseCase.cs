using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record CreateDebtCommand(
    string Name,
    DebtKind Kind,
    Money InitialAmount,
    Money InstallmentAmount);

public sealed class CreateDebtUseCase(IDebtRepository debts, IUnitOfWork uow)
{
    public async Task<DebtId> ExecuteAsync(CreateDebtCommand cmd, CancellationToken ct = default)
    {
        var debt = Debt.Create(cmd.Name, cmd.Kind, cmd.InitialAmount, cmd.InstallmentAmount);
        await debts.AddAsync(debt, ct);
        await uow.SaveChangesAsync(ct);
        return debt.Id;
    }
}
