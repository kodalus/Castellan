using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Chowa konto, nie niszcząc niczego: zarchiwizowane konto wypada z list wyboru i z sald,
/// ale jego transakcje zostają tam, gdzie były — w kopertach swoich miesięcy,
/// w statystykach i w średniej pod poduszką.
///
/// To jest właściwa odpowiedź na „to konto już go nie używam", a <see cref="DeleteAccountUseCase"/>
/// na „tego konta nigdy nie powinno tu być". Dwie różne rzeczy, choć na ekranie startują
/// z tego samego przycisku.
/// </summary>
public sealed class ArchiveAccountUseCase(IAccountRepository accounts, IUnitOfWork uow)
{
    public async Task ExecuteAsync(AccountId id, CancellationToken ct = default)
    {
        var account = await accounts.GetAsync(id, ct);
        if (account is null) return;

        account.Archive();
        await uow.SaveChangesAsync(ct);
    }
}
