using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Przeniesienie odłożonych pieniędzy z jednego celu na drugi.
///
/// NIE POWSTAJE ŻADNA TRANSAKCJA — i to jest cała istota tej operacji. Fundusz jest
/// kopertą nad pieniędzmi, które fizycznie leżą na koncie; przełożenie między dwiema
/// kopertami nie rusza ani salda konta, ani budżetu miesiąca. Zmieniają się wyłącznie
/// salda dwóch funduszy.
///
/// Obejście przez dwa przelewy między kontami (oszczędnościowe → rozliczeniowe →
/// oszczędnościowe) daje ten sam efekt na funduszach, ale zostawia CZTERY wpisy
/// w historii konta, które nic nie opisują: żadne pieniądze nigdzie nie poszły.
/// </summary>
public sealed class MoveBetweenFundsUseCase(IFundRepository funds, IUnitOfWork uow)
{
    public async Task ExecuteAsync(FundId from, FundId to, Money amount, CancellationToken ct = default)
    {
        if (from == to)
            throw new InvalidOperationException("Fundusz źródłowy i docelowy muszą być różne.");

        var magnitude = Math.Abs(amount.Grosze);
        if (magnitude == 0)
            throw new InvalidOperationException("Kwota musi być większa od zera.");

        var source = await funds.GetAsync(from, ct)
            ?? throw new InvalidOperationException($"Fund {from} not found.");
        var target = await funds.GetAsync(to, ct)
            ?? throw new InvalidOperationException($"Fund {to} not found.");

        if (magnitude > source.Balance.Grosze)
            throw new InvalidOperationException(
                $"W funduszu „{source.Name}” jest {source.Balance}, a przenosisz {new Money(magnitude)}.");

        source.Withdraw(new Money(magnitude));

        // Contribute, a nie samo podbicie salda: dla funduszu docelowego to jest wpłata
        // tego miesiąca i ma zamykać bieżący okres, żeby podpowiadana rata nie kazała
        // dokładać drugi raz na cel, który właśnie dostał pieniądze.
        target.Contribute(new Money(magnitude));

        await uow.SaveChangesAsync(ct);
    }
}
