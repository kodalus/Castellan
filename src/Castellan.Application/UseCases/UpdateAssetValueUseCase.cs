using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class UpdateAssetValueUseCase(IAssetRepository assets, IUnitOfWork uow)
{
    public async Task ExecuteAsync(AssetId id, Money newValue, CancellationToken ct = default)
    {
        var asset = await assets.GetAsync(id, ct)
            ?? throw new InvalidOperationException("Nie znaleziono aktywa.");
        asset.UpdateValue(newValue);
        await uow.SaveChangesAsync(ct);
    }
}
