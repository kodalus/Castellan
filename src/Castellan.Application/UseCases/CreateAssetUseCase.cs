using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record CreateAssetCommand(string Name, AssetLiquidity Liquidity, Money Value);

public sealed class CreateAssetUseCase(IAssetRepository assets, IUnitOfWork uow)
{
    public async Task ExecuteAsync(CreateAssetCommand cmd, CancellationToken ct = default)
    {
        var asset = Asset.Create(cmd.Name, cmd.Liquidity, cmd.Value);
        await assets.AddAsync(asset, ct);
        await uow.SaveChangesAsync(ct);
    }
}
