using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class UpdateTransactionUseCase(
    IAccountRepository accounts,
    ICategoryRepository categories,
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    public sealed record Input(
        TransactionId Id,
        AccountId AccountId,
        Money Amount,
        DateTimeOffset OccurredAt,
        CategoryId CategoryId,
        string? Note);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(input.Id, ct)
            ?? throw new InvalidOperationException($"Transaction {input.Id} not found.");

        var account = await accounts.GetAsync(input.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {input.AccountId} not found.");
        if (account.IsArchived)
            throw new InvalidOperationException("Cannot assign transaction to archived account.");

        var category = await categories.GetAsync(input.CategoryId, ct)
            ?? throw new InvalidOperationException($"Category {input.CategoryId} not found.");
        if (category.IsArchived)
            throw new InvalidOperationException("Cannot assign archived category.");

        tx.SetAccount(input.AccountId);
        tx.SetAmount(input.Amount);
        tx.SetOccurredAt(input.OccurredAt);
        tx.AssignCategory(input.CategoryId);
        tx.SetNote(input.Note);

        await uow.SaveChangesAsync(ct);
    }
}
