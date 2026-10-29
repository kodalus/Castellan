using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Zakończenie funduszu: cel się wydarzył (OC zapłacone, urlop wzięty) i koperta
/// nie ma już czego pilnować.
///
/// To NIE jest usunięcie. Zakończony fundusz znika z list wyboru i z sumy odpisów,
/// ale zostaje razem ze swoim saldem i — co ważniejsze — razem z transakcjami, które
/// z niego pokryto. <see cref="DeleteFundUseCase"/> odpina te transakcje, przez co
/// dawne wydatki wracają do kopert i miesiące sprzed roku nagle pokazują inne kwoty.
/// Zakończenie nie rusza historii, więc to ono jest właściwą odpowiedzią na
/// „ten fundusz już zrobił swoje”.
///
/// Reszta salda nie może zniknąć po cichu: albo jedzie na inny cel (i wtedy
/// zachowuje się dokładnie jak <see cref="MoveBetweenFundsUseCase"/> — żadnej
/// transakcji, pieniądze i tak leżą na tym samym koncie), albo przestaje być
/// odłożona i wraca do puli wolnych pieniędzy. Obie drogi są świadomym wyborem
/// na ekranie, bo zgubienie tysiąca złotych przy zamykaniu koperty to nie detal.
/// </summary>
public sealed class CloseFundUseCase(IFundRepository funds, IUnitOfWork uow)
{
    /// <param name="moveRemainderTo">
    /// Fundusz, na który ma pójść reszta salda, albo <c>null</c>, gdy reszta ma po prostu
    /// przestać być odłożona.
    /// </param>
    public async Task ExecuteAsync(FundId id, FundId? moveRemainderTo = null, CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Fund {id} not found.");

        if (moveRemainderTo is { } targetId)
        {
            if (targetId == id)
                throw new InvalidOperationException("Reszty nie da się przenieść na kończony fundusz.");

            var target = await funds.GetAsync(targetId, ct)
                ?? throw new InvalidOperationException($"Fund {targetId} not found.");

            if (target.IsArchived)
                throw new InvalidOperationException(
                    $"Fundusz „{target.Name}” jest zakończony — nie przyjmie reszty.");

            var remainder = fund.Balance;
            if (remainder.Grosze > 0)
            {
                fund.Withdraw(remainder);
                // Contribute, nie samo podbicie salda: dla celu docelowego to jest wpłata
                // tego miesiąca i ma zamknąć bieżący okres, żeby podpowiadana rata nie
                // kazała dokładać drugi raz na cel, który właśnie dostał pieniądze.
                target.Contribute(remainder);
            }
        }

        fund.Archive();
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Przywraca zakończony fundusz do listy aktywnych — razem z saldem, jakie mu zostało.</summary>
    public async Task ReopenAsync(FundId id, CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Fund {id} not found.");

        fund.Reopen();
        await uow.SaveChangesAsync(ct);
    }
}
