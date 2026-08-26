using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Podnosi saldo funduszu i nic więcej.
///
/// Był tu kiedyś wariant z kontem, który dopisywał wydatek w kategorii „Rezerwy".
/// Zapisywał jednak tylko POŁOWĘ zdarzenia: ile ubyło z konta źródłowego, bez tego,
/// gdzie pieniądze wylądowały. Saldo jednego konta malało, drugiego nie rosło, a suma
/// majątku spadała, mimo że pieniądze nadal były. Wariant usunięty — ruch pieniędzy
/// zapisuje się przelewem (<see cref="CreateTransferUseCase"/> z IsReserve), który ma
/// obie strony.
/// </summary>
public sealed class ContributeToFundUseCase(IFundRepository funds, IUnitOfWork uow)
{
    public async Task ExecuteAsync(FundId id, Money amount, CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Fund {id} not found.");

        fund.Contribute(amount);
        await uow.SaveChangesAsync(ct);
    }
}
