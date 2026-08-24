using Castellan.Application.Repositories;
using Castellan.Domain;

namespace Castellan.Application.UseCases;

/// <summary>
/// Usuwa aktywo na dobre. Aktywo jest liczbą, a nie historią: żadna transakcja ani
/// żaden inny agregat się do niego nie odwołuje, więc nie ma czego osierocić — inaczej
/// niż przy koncie czy kategorii, które są archiwizowane właśnie dlatego, że wiszą na
/// nich transakcje.
///
/// Skutek jest jeden i widoczny od razu: poduszka finansowa traci wartość tego aktywa,
/// a wraz z nią spada liczba miesięcy. Dlatego ekran pyta o potwierdzenie i podaje kwotę.
/// </summary>
public sealed class DeleteAssetUseCase(IAssetRepository assets, IUnitOfWork uow)
{
    public async Task ExecuteAsync(AssetId id, CancellationToken ct = default)
    {
        var asset = await assets.GetAsync(id, ct);
        if (asset is null) return;

        await assets.RemoveAsync(asset, ct);
        await uow.SaveChangesAsync(ct);
    }
}
