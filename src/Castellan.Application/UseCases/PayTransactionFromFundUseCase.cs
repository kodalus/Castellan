using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Pokrywa wydatek z funduszu: zdejmuje kwotę z salda funduszu i wyłącza
/// transakcję z kopert miesiąca (odpisy obciążyły budżet już wcześniej).
/// </summary>
public sealed class PayTransactionFromFundUseCase(
    ITransactionRepository transactions,
    IFundRepository funds,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(TransactionId transactionId, FundId fundId, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(transactionId, ct)
            ?? throw new InvalidOperationException($"Transaction {transactionId} not found.");
        var fund = await funds.GetAsync(fundId, ct)
            ?? throw new InvalidOperationException($"Fund {fundId} not found.");

        // Zmiana funduszu: najpierw zwróć kwotę poprzedniemu.
        if (tx.PaidFromFundId is { } previousId)
        {
            if (previousId == fundId) return;
            var previous = await funds.GetAsync(previousId, ct);
            previous?.Contribute(new Money(Math.Abs(tx.Amount.Grosze)));
        }

        fund.Withdraw(new Money(Math.Abs(tx.Amount.Grosze)));
        tx.PayFromFund(fundId);

        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Cofa pokrycie z funduszu — kwota wraca na saldo, wydatek znów liczy się do kopert.</summary>
    public async Task UndoAsync(TransactionId transactionId, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(transactionId, ct)
            ?? throw new InvalidOperationException($"Transaction {transactionId} not found.");

        if (tx.PaidFromFundId is not { } fundId) return;

        var fund = await funds.GetAsync(fundId, ct);
        fund?.Contribute(new Money(Math.Abs(tx.Amount.Grosze)));
        tx.ClearFundPayment();

        await uow.SaveChangesAsync(ct);
    }
}
