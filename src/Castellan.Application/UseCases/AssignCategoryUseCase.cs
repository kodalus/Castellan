using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Application;

namespace Castellan.Application.UseCases;

public sealed class AssignCategoryUseCase(
    ITransactionRepository transactions,
    ICategoryRuleRepository categoryRules,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(
        TransactionId transactionId,
        CategoryId categoryId,
        bool rememberRule,
        CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(transactionId, ct)
            ?? throw new InvalidOperationException($"Transaction {transactionId.Value} not found");

        tx.AssignCategory(categoryId);

        if (rememberRule)
        {
            var key = tx.MerchantKey ?? MerchantKeyNormalizer.Normalize(tx.RawMerchant);
            if (!string.IsNullOrEmpty(key))
            {
                var rules = await categoryRules.ListAsync(ct);
                var existing = rules.FirstOrDefault(r =>
                    r.Pattern.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                    await categoryRules.AddAsync(
                        CategoryRule.Create(key, categoryId, CategoryRuleOrigin.Learned), ct);
                else
                    existing.UpdateCategory(categoryId);
            }
        }

        await uow.SaveChangesAsync(ct);
    }
}
