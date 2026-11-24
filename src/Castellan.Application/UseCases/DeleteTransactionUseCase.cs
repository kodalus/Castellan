using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class DeleteTransactionUseCase(
    ITransactionRepository transactions,
    IFundRepository funds,
    IUnitOfWork uow)
{
    /// <summary>
    /// Zwraca identyfikatory wpisów, które naprawdę zniknęły — dla przelewu są to DWA
    /// wpisy, nie jeden. Bez tego ekran usuwał ze swojej listy tylko dotknięty wiersz,
    /// druga noga zostawała widoczna, a tapnięcie w nią kończyło się wyjątkiem
    /// „Transaction … not found" o czymś, czego już nie było.
    ///
    /// Brak transakcji nie jest błędem: dwa szybkie przesunięcia albo nieodświeżona
    /// lista to zwykłe sytuacje, a nie awaria. Tak samo zachowuje się usuwanie aktywa.
    /// </summary>
    /// <param name="removeProposedPair">
    /// Dotyczy przelewu WYKRYTEGO, ale jeszcze niepotwierdzonego. Para z propozycji może
    /// być prawdziwym przelewem (wtedy obie strony mają zniknąć razem) albo przypadkowym
    /// zestawieniem dwóch niezwiązanych kwot (wtedy druga strona jest prawdziwą
    /// transakcją i nie wolno jej ruszać). Tego nie da się rozstrzygnąć w kodzie —
    /// decyduje użytkownik.
    /// </param>
    public async Task<IReadOnlyList<TransactionId>> ExecuteAsync(
        TransactionId id, bool removeProposedPair = false, CancellationToken ct = default)
    {
        var tx = await transactions.GetAsync(id, ct);
        if (tx is null) return [];

        // Przelew to para wpisów na dwóch kontach. Skasowanie jednej strony
        // zostawiłoby drugą jako sierotę: nadal wyłączoną z kopert, ale bez pary,
        // przez co saldo jednego konta zmieniłoby się bez odpowiednika na drugim.
        var removed = new List<Domain.Aggregates.Transaction>();
        if (tx.TransferGroupId is { } groupId)
        {
            foreach (var leg in await transactions.ListByTransferGroupAsync(groupId, ct))
            {
                await transactions.RemoveAsync(leg, ct);
                removed.Add(leg);
            }
        }
        // Przelew wykryty, ale jeszcze niepotwierdzony, trzyma pare pod INNYM polem —
        // ProposedTransferGroupId zamiast TransferGroupId. Tego przypadku tu nie bylo,
        // wiec usuwalo sie tylko dotknieta noge, a druga zostawala na swoim koncie:
        // saldo nie wracalo do stanu sprzed przelewu, mimo ze przelew "zniknal".
        else if (tx.ProposedTransferGroupId is { } proposedId)
        {
            var legs = (await transactions.ListProposedTransfersAsync(ct))
                .Where(t => t.ProposedTransferGroupId == proposedId)
                .ToList();

            foreach (var leg in legs)
            {
                if (removeProposedPair || leg.Id == tx.Id)
                {
                    await transactions.RemoveAsync(leg, ct);
                    removed.Add(leg);
                }
                else
                {
                    // Noga, ktora zostaje, nie moze zostac z propozycja wskazujaca na
                    // grupe bez pary — takiej propozycji nie da sie juz potwierdzic
                    // i wpis utknalby z napisem "potwierdz w Skrzynce" na zawsze.
                    leg.ClearTransferProposal();
                }
            }
        }
        else
        {
            await transactions.RemoveAsync(tx, ct);
            removed.Add(tx);
        }

        foreach (var gone in removed)
            await ReverseFundEffectAsync(gone, ct);

        await uow.SaveChangesAsync(ct);
        return [.. removed.Select(t => t.Id)];
    }

    /// <summary>
    /// Saldo funduszu jest osobną, zapisaną liczbą — nie wynika z transakcji. Dopóki
    /// kasowanie go nie dotykało, usunięty przelew zostawiał fundusz podniesiony o kwotę,
    /// której już nigdzie nie było: po usunięciu zdublowanego przelewu na 7 000 zł
    /// poduszka pokazywała o 7 000 zł za dużo, a jedynym ratunkiem była ręczna poprawka
    /// salda funduszu.
    /// </summary>
    private async Task ReverseFundEffectAsync(Domain.Aggregates.Transaction gone, CancellationToken ct)
    {
        var magnitude = new Money(Math.Abs(gone.Amount.Grosze));

        // Odkładanie i wyjmowanie dzielą jedno pole, bo kierunek mówi znak kwoty.
        // Pieniądze wychodzące z konta do wydawania (ujemne) poszły DO funduszu, więc
        // cofnięcie je stamtąd zabiera; przychodzące (dodatnie) z funduszu wyszły, więc
        // cofnięcie je tam zwraca.
        if (gone.ReserveFundId is { } reserveId && await funds.GetAsync(reserveId, ct) is { } reserveFund)
        {
            if (gone.Amount.IsNegative) reserveFund.Withdraw(magnitude);
            else                        reserveFund.Contribute(magnitude);
        }

        // Wydatek pokryty z funduszu zdjął kwotę z jego salda. Skasowanie wydatku musi
        // ją oddać — inaczej fundusz zostaje uszczuplony o zakup, którego już nie ma.
        if (gone.PaidFromFundId is { } paidId && await funds.GetAsync(paidId, ct) is { } paidFund)
            paidFund.Contribute(magnitude);
    }
}
