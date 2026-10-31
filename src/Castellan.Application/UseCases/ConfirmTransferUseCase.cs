using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class ConfirmTransferUseCase(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IFundRepository funds,
    IUnitOfWork uow)
{
    public const string ReserveCategoryName = "Rezerwy";

    /// <summary>Zwykły przelew między własnymi kontami — obie nogi wypadają z budżetu.</summary>
    public async Task ExecuteAsync(Guid proposedGroupId, CancellationToken ct = default)
    {
        var pair = await FindPairAsync(proposedGroupId, ct);
        if (pair is null) return;

        var groupId = Guid.NewGuid();
        pair.Value.From.SetTransferGroup(groupId);
        pair.Value.To.SetTransferGroup(groupId);

        await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Odkładanie na rezerwę: noga wychodząca zostaje wydatkiem w kategorii „Rezerwy",
    /// noga przychodząca wypada z budżetu. Gdy podany jest fundusz, jego saldo rośnie
    /// o tę samą kwotę — dzięki temu jedna czynność w banku zamyka temat w aplikacji.
    /// </summary>
    public async Task ExecuteAsReserveAsync(Guid proposedGroupId, FundId? contributeTo, CancellationToken ct = default)
    {
        var pair = await FindPairAsync(proposedGroupId, ct);
        if (pair is null) return;

        var reserve = (await categories.ListAsync(ct))
            .FirstOrDefault(c => c.Name.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase));

        // Bez kategorii „Rezerwy" nie ma czego obciążyć — wtedy zwykły przelew jest
        // bezpieczniejszy niż wydatek wrzucony do przypadkowej kategorii.
        if (reserve is null)
        {
            await ExecuteAsync(proposedGroupId, ct);
            return;
        }

        var (from, to) = pair.Value;
        from.MarkAsReserveMove(reserve.Id);
        to.SetTransferGroup(Guid.NewGuid());

        if (contributeTo is { } fundId)
        {
            var fund = await funds.GetAsync(fundId, ct);
            fund?.Contribute(new Money(Math.Abs(from.Amount.Grosze)));
        }

        await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Wyjęcie odłożonych pieniędzy — dokładne odwrócenie odkładania. Noga przychodząca,
    /// ta lądująca na koncie do wydawania, zostaje wpisem w kategorii „Rezerwy" z kwotą
    /// DODATNIĄ, więc koperta maleje o tyle, ile zabrano. Maleje też saldo funduszu,
    /// bo odkładanie na ten cel się skończyło.
    ///
    /// Wcześniej obie nogi wypadały z budżetu jak przy zwykłym przelewie, w obawie przed
    /// policzeniem odpisu drugi raz. To było pomylenie dwóch rzeczy: odpisu z minionego
    /// miesiąca rzeczywiście nie wolno cofać wstecz, ale pieniądze wracają do wydawania
    /// W MIESIĄCU, W KTÓRYM JE WYJĘTO — i tam muszą być widoczne. Bez tego koperta
    /// Rezerwy pokazywała samą sumę wpłat: „19 000 odłożone" przy niespełna połowie tej
    /// kwoty naprawdę odłożonej, a budżet miesiąca był zaniżony o całą resztę.
    ///
    /// Sumarycznie nic się nie dubluje: odkładanie zabiera z jednego miesiąca, wyjęcie
    /// oddaje do drugiego, a per saldo odłożone zostaje tyle, ile zostało.
    ///
    /// Odpowiednik tego, co przy ręcznym przelewie robi CreateTransferUseCase: ta sama
    /// decyzja musi być do podjęcia niezależnie od tego, czy przelew wpisano samemu,
    /// czy złapały go powiadomienia.
    /// </summary>
    public async Task ExecuteWithWithdrawalAsync(
        Guid proposedGroupId, FundId? withdrawFrom, CancellationToken ct = default)
    {
        // Para szukana PRZED potwierdzeniem: potwierdzenie czyści propozycję, więc potem
        // nie ma już po czym jej znaleźć — a kwotę bierzemy z transakcji, nie z ekranu.
        var pair = await FindPairAsync(proposedGroupId, ct);
        if (pair is null) return;

        var kwota = new Money(Math.Abs(pair.Value.From.Amount.Grosze));

        var reserve = (await categories.ListAsync(ct))
            .FirstOrDefault(c => c.Name.Equals(ReserveCategoryName, StringComparison.OrdinalIgnoreCase));

        var groupId = Guid.NewGuid();
        pair.Value.From.SetTransferGroup(groupId);

        // Bez kategorii „Rezerwy" nie ma czego uznawać — wtedy zwykły przelew jest
        // bezpieczniejszy niż wpis wrzucony do przypadkowej kategorii.
        if (reserve is not null)
            pair.Value.To.MarkAsReserveMove(reserve.Id);
        else
            pair.Value.To.SetTransferGroup(groupId);

        if (withdrawFrom is { } fundId)
        {
            var fund = await funds.GetAsync(fundId, ct);
            fund?.Withdraw(kwota);
        }

        await uow.SaveChangesAsync(ct);
    }

    private async Task<(Domain.Aggregates.Transaction From, Domain.Aggregates.Transaction To)?>
        FindPairAsync(Guid proposedGroupId, CancellationToken ct)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var pair = proposed.Where(t => t.ProposedTransferGroupId == proposedGroupId).ToList();
        if (pair.Count != 2) return null;

        var from = pair[0].Amount.IsNegative ? pair[0] : pair[1];
        var to = pair[0].Amount.IsNegative ? pair[1] : pair[0];
        return (from, to);
    }
}
