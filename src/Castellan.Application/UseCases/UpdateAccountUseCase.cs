using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Zmiana nazwy, typu i banku konta. Nazwa nie jest identyfikatorem — transakcje wiszą
/// na <see cref="AccountId"/> — więc przemianowanie nie rusza ani jednej z nich.
/// </summary>
public sealed class UpdateAccountUseCase(IAccountRepository accounts, IUnitOfWork uow)
{
    public sealed record Input(AccountId Id, string Name, AccountKind Kind, string? BankKey);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

        var account = await accounts.GetAsync(input.Id, ct);
        if (account is null) return;

        account.Rename(input.Name);
        account.SetKind(input.Kind);
        account.SetBank(input.BankKey);

        await uow.SaveChangesAsync(ct);
    }
}
