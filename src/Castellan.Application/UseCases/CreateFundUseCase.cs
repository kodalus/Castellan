using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record CreateFundCommand(
    string Name,
    FundKind Kind,
    Money TargetAmount,
    DateOnly? Deadline);

public sealed class CreateFundUseCase(IFundRepository funds, IUnitOfWork uow)
{
    public async Task<FundId> ExecuteAsync(CreateFundCommand cmd, CancellationToken ct = default)
    {
        var fund = Fund.Create(cmd.Name, cmd.Kind, cmd.TargetAmount, cmd.Deadline);
        await funds.AddAsync(fund, ct);
        await uow.SaveChangesAsync(ct);
        return fund.Id;
    }
}
