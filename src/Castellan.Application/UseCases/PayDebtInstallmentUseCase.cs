using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Spłata raty ma dwa skutki naraz i oba muszą się wydarzyć: rata to realny wydatek
/// miesiąca (obciąża kopertę) ORAZ zmniejsza saldo zobowiązania. Rozdzielenie tego na
/// dwie osobne czynności gwarantowałoby, że prędzej czy później zrobi się jedną
/// i zapomni o drugiej.
/// </summary>
public sealed class PayDebtInstallmentUseCase(
    IDebtRepository debts,
    IAccountRepository accounts,
    ICategoryRepository categories,
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public sealed record Input(
        DebtId DebtId,
        AccountId AccountId,
        CategoryId CategoryId,
        Money Amount,
        DateTimeOffset OccurredAt,
        string? Note = null);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        var magnitude = Math.Abs(input.Amount.Grosze);
        if (magnitude == 0)
            throw new InvalidOperationException("Kwota raty musi być większa od zera.");

        var debt = await debts.GetAsync(input.DebtId, ct)
            ?? throw new InvalidOperationException($"Debt {input.DebtId} not found.");

        var account = await accounts.GetAsync(input.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {input.AccountId} not found.");
        if (account.IsArchived)
            throw new InvalidOperationException("Nie można zapłacić z zarchiwizowanego konta.");

        var category = await categories.GetAsync(input.CategoryId, ct)
            ?? throw new InvalidOperationException($"Category {input.CategoryId} not found.");
        if (category.IsArchived)
            throw new InvalidOperationException("Nie można użyć zarchiwizowanej kategorii.");

        var tx = Transaction.CreateManual(
            input.AccountId,
            new Money(-magnitude),
            input.OccurredAt,
            input.CategoryId,
            input.Note ?? $"Rata: {debt.Name}");

        await transactions.AddAsync(tx, ct);
        debt.Pay(new Money(magnitude));

        await uow.SaveChangesAsync(ct);
    }
}
