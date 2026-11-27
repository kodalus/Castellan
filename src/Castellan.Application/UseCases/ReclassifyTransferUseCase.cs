using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>Czym naprawdę był ten przelew między własnymi kontami.</summary>
public enum TransferClassification
{
    /// <summary>Zwykłe przekładanie pieniędzy — budżet i fundusze bez zmian.</summary>
    Plain,

    /// <summary>Odkładanie: pieniądze przestają być do wydania, koperta „Rezerwy" rośnie.</summary>
    Reserve,

    /// <summary>Wyjmowanie odłożonych: wracają do wydania, koperta „Rezerwy" maleje.</summary>
    ReserveReturn,
}

/// <summary>
/// Zmiana rodzaju JUŻ ZAPISANEGO przelewu.
///
/// Pytanie „odkładasz czy przekładasz?" pada raz, w momencie zapisu, i dotąd nie dało
/// się go odwołać. Jedna pomyłka — 600 zł wyjęte z oszczędności zapisane jako zwykłe
/// przełożenie — wypadała z koperty „Rezerwy" na zawsze, a jedynym ratunkiem było
/// skasowanie przelewu i wpisanie go od nowa. Czyli najbardziej ryzykowna operacja
/// w aplikacji po to, żeby poprawić jedno kliknięcie.
///
/// Zmiana obejmuje OBIE nogi i saldo funduszu: najpierw cofa to, co poprzedni rodzaj
/// zrobił funduszowi, potem stosuje nowy. Dzięki temu przełączanie tam i z powrotem
/// nie zostawia śladu.
/// </summary>
public sealed class ReclassifyTransferUseCase(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IFundRepository funds,
    IUnitOfWork uow)
{
    public sealed record Input(
        TransactionId AnyLegId,
        TransferClassification Classification,
        /// <summary>Fundusz, którego saldo ma pójść za zmianą. Dobrowolny.</summary>
        FundId? Fund = null);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        var leg = await transactions.GetAsync(input.AnyLegId, ct)
            ?? throw new InvalidOperationException("Tej transakcji już nie ma.");

        var pair = await FindPairAsync(leg, ct)
            ?? throw new InvalidOperationException(
                "Nie znaleziono drugiej strony tego przelewu. Zmiana rodzaju tylko jednej "
                + "nogi rozjechałaby salda kont, więc nic nie zmieniam.");

        var (outgoing, incoming) = pair;

        // Najpierw czysta kartka: cofnij to, co obecny rodzaj zrobił funduszowi.
        // Bez tego przełączenie „odkładam" → „przekładam" zostawiałoby w funduszu
        // pieniądze, które przestały być odkładane.
        foreach (var side in new[] { outgoing, incoming })
        {
            if (side.ReserveFundId is { } previousId && await funds.GetAsync(previousId, ct) is { } previous)
            {
                var undo = new Money(Math.Abs(side.Amount.Grosze));
                if (side.Amount.IsNegative) previous.Withdraw(undo);
                else                        previous.Contribute(undo);
            }
            side.ClearReserveFund();
        }

        var groupId = outgoing.TransferGroupId ?? incoming.TransferGroupId ?? Guid.NewGuid();
        var magnitude = new Money(Math.Abs(outgoing.Amount.Grosze));

        var reserve = (await categories.ListAsync(ct))
            .FirstOrDefault(c => c.Name.Equals(
                ConfirmTransferUseCase.ReserveCategoryName, StringComparison.OrdinalIgnoreCase));

        // Bez kategorii „Rezerwy" nie ma czego obciążyć — wtedy zwykły przelew jest
        // bezpieczniejszy niż wpis wrzucony do przypadkowej kategorii.
        var classification = reserve is null ? TransferClassification.Plain : input.Classification;

        switch (classification)
        {
            case TransferClassification.Reserve:
                outgoing.MarkAsReserveMove(reserve!.Id, groupId);
                incoming.SetTransferGroup(groupId);
                if (input.Fund is { } contributeTo && await funds.GetAsync(contributeTo, ct) is { } target)
                {
                    target.Contribute(magnitude);
                    outgoing.SetReserveFund(contributeTo);
                }
                break;

            case TransferClassification.ReserveReturn:
                outgoing.SetTransferGroup(groupId);
                incoming.MarkAsReserveMove(reserve!.Id, groupId);
                if (input.Fund is { } withdrawFrom && await funds.GetAsync(withdrawFrom, ct) is { } source)
                {
                    source.Withdraw(magnitude);
                    incoming.SetReserveFund(withdrawFrom);
                }
                break;

            default:
                outgoing.SetTransferGroup(groupId);
                incoming.SetTransferGroup(groupId);
                break;
        }

        await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Obie nogi przelewu. Zwykle wystarczy wspólny TransferGroupId, ale wpisy sprzed
    /// powiązania nóg go nie mają: noga w „Rezerwach" była dla aplikacji osobnym
    /// wydatkiem. Dla nich zostaje ślad z momentu zapisu — ta sama co do taktu chwila
    /// i kwota dokładnie przeciwna, bo obie powstały w jednym zapisie.
    /// </summary>
    private async Task<(Domain.Aggregates.Transaction Outgoing, Domain.Aggregates.Transaction Incoming)?>
        FindPairAsync(Domain.Aggregates.Transaction leg, CancellationToken ct)
    {
        var legs = leg.TransferGroupId is { } groupId
            ? (await transactions.ListByTransferGroupAsync(groupId, ct)).ToList()
            : [];

        if (legs.Count != 2)
        {
            var sameMonth = await transactions.ListForMonthAsync(YearMonth.From(leg.OccurredAt), ct);
            var counterpart = sameMonth.FirstOrDefault(t =>
                t.Id != leg.Id &&
                t.AccountId != leg.AccountId &&
                t.OccurredAt == leg.OccurredAt &&
                t.Amount.Grosze == -leg.Amount.Grosze &&
                !t.SupersededById.HasValue);

            legs = counterpart is null ? [leg] : [leg, counterpart];
        }

        if (legs.Count != 2) return null;

        var outgoing = legs[0].Amount.IsNegative ? legs[0] : legs[1];
        var incoming = legs[0].Amount.IsNegative ? legs[1] : legs[0];

        // Dwie nogi o tym samym znaku to nie przelew — lepiej nic nie ruszać.
        return outgoing.Amount.IsNegative && !incoming.Amount.IsNegative
            ? (outgoing, incoming)
            : null;
    }
}
