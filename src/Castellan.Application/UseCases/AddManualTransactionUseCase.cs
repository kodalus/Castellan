using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class AddManualTransactionUseCase(
    IAccountRepository accounts,
    ICategoryRepository categories,
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public sealed record Input(
        AccountId AccountId,
        Money Amount,
        DateTimeOffset OccurredAt,
        CategoryId CategoryId,
        string? Note = null);

    public async Task<TransactionId> ExecuteAsync(Input input, CancellationToken ct = default)
    {
        var account = await accounts.GetAsync(input.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {input.AccountId} not found.");
        if (account.IsArchived)
            throw new InvalidOperationException("Cannot add transaction to archived account.");

        var category = await categories.GetAsync(input.CategoryId, ct)
            ?? throw new InvalidOperationException($"Category {input.CategoryId} not found.");
        if (category.IsArchived)
            throw new InvalidOperationException("Cannot assign archived category.");

        var tx = Transaction.CreateManual(
            input.AccountId, input.Amount, input.OccurredAt, input.CategoryId, input.Note);

        await transactions.AddAsync(tx, ct);
        await uow.SaveChangesAsync(ct);
        return tx.Id;
    }
}
