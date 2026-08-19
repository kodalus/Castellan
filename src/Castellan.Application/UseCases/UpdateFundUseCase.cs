using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record UpdateFundCommand(
    FundId Id,
    string Name,
    FundKind Kind,
    Money TargetAmount,
    DateOnly? Deadline);

public sealed class UpdateFundUseCase(IFundRepository funds, IUnitOfWork uow)
{
    public async Task ExecuteAsync(UpdateFundCommand cmd, CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(cmd.Id, ct)
            ?? throw new InvalidOperationException($"Fund {cmd.Id} not found.");

        fund.Update(cmd.Name, cmd.Kind, cmd.TargetAmount, cmd.Deadline);

        await uow.SaveChangesAsync(ct);
    }
}
