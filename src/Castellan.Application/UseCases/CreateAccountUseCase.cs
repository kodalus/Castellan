using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class CreateAccountUseCase(IAccountRepository accounts, IUnitOfWork uow)
{
    public sealed record Input(
        string Name,
        AccountKind Kind,
        Money InitialBalance,
        DateTimeOffset ReconciledAt);

    public async Task<AccountId> ExecuteAsync(Input input, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        var account = Account.Create(input.Name, input.Kind, input.InitialBalance, input.ReconciledAt);
        await accounts.AddAsync(account, ct);
        await uow.SaveChangesAsync(ct);
        return account.Id;
    }
}
