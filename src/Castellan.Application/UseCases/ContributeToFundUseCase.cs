using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

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
