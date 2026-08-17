using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record UpdateDebtCommand(
    DebtId Id,
    string Name,
    DebtKind Kind,
    Money InitialAmount,
    Money InstallmentAmount,
    Money Balance);

public sealed class UpdateDebtUseCase(IDebtRepository debts, IUnitOfWork uow)
{
    public async Task ExecuteAsync(UpdateDebtCommand cmd, CancellationToken ct = default)
    {
        var debt = await debts.GetAsync(cmd.Id, ct)
            ?? throw new InvalidOperationException($"Debt {cmd.Id} not found.");

        debt.Update(cmd.Name, cmd.Kind, cmd.InitialAmount, cmd.InstallmentAmount);
        // Saldo edytowalne wprost: odsetki, prowizje i wakacje kredytowe potrafią je
        // rozjechać względem sumy zapłaconych rat, więc musi dać się je poprawić
        // do tego, co pokazuje bank.
        debt.SetBalance(cmd.Balance);

        await uow.SaveChangesAsync(ct);
    }
}
