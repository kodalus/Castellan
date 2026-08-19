using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Przełącza, czy fundusz wchodzi do poduszki finansowej. Osobny scenariusz, a nie
/// część UpdateFundUseCase, bo przełącza się go jednym tapnięciem wprost z listy —
/// bez otwierania formularza i bez ryzyka, że przy okazji nadpisze się resztę pól
/// stanem z ekranu, którego użytkownik nawet nie widział.
/// </summary>
public sealed class SetFundCushionFlagUseCase(IFundRepository funds, IUnitOfWork uow)
{
    public async Task ExecuteAsync(FundId id, bool countsTowardCushion, CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Fund {id} not found.");

        fund.SetCountsTowardCushion(countsTowardCushion);
        await uow.SaveChangesAsync(ct);
    }
}
