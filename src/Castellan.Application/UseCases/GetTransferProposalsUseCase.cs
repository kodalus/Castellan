using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record TransferProposalOverview(
    Guid GroupId,
    string FromAccountName,
    string ToAccountName,
    Money Amount,
    DateTimeOffset OccurredAt,
    bool ToIsSavings,
    /// <summary>
    /// Pieniądze OPUSZCZAJĄ oszczędności. Kierunek odwrotny do odkładania, więc pytanie
    /// jest inne: nie „czy odkładasz", tylko „czy wyjmujesz z funduszu".
    /// </summary>
    bool LeavesSavings,
    /// <summary>
    /// Czy propozycja ma komplet dwóch nóg. Bez kompletu potwierdzenie NIC NIE ROBI —
    /// nie ma czego spiąć w parę — więc jedyne sensowne wyjście to odrzucenie, które
    /// zdejmuje oznaczenie i przywraca wpis do normalnego obiegu.
    /// </summary>
    bool CanConfirm);

public sealed class GetTransferProposalsUseCase(
    ITransactionRepository transactions,
    IAccountRepository accounts)
{
    public async Task<IReadOnlyList<TransferProposalOverview>> ExecuteAsync(CancellationToken ct = default)
    {
        var proposed = await transactions.ListProposedTransfersAsync(ct);
        var allAccounts = await accounts.ListAsync(ct);
        var accountMap = allAccounts.ToDictionary(a => a.Id, a => a);

        var result = new List<TransferProposalOverview>();

        foreach (var group in proposed.GroupBy(t => t.ProposedTransferGroupId!.Value))
        {
            var legs = group.ToList();

            // Grupa bez kompletu dwóch nóg BYŁA tu wcześniej pomijana — i to była cicha
            // pułapka: oznaczenie zostawało na transakcji, wiersz w Transakcjach pisał
            // „potwierdź w Skrzynce", a Skrzynka była pusta. Potwierdzenie i tak nic by
            // nie dało, bo nie ma czego spiąć w parę. Taka propozycja musi być widoczna,
            // żeby dało się ją odrzucić i uwolnić wpis.
            var paired = legs.Count == 2;

            var from = legs.FirstOrDefault(t => t.Amount.IsNegative) ?? legs[0];
            var to   = legs.FirstOrDefault(t => !t.Amount.IsNegative);

            var fromAccount = accountMap.GetValueOrDefault(from.AccountId);
            var toAccount   = to is null ? null : accountMap.GetValueOrDefault(to.AccountId);

            result.Add(new TransferProposalOverview(
                group.Key,
                fromAccount?.Name ?? "?",
                toAccount?.Name ?? "?",
                new Money(Math.Abs(from.Amount.Grosze)),
                from.OccurredAt,
                // Przelew NA własne oszczędnościowe to zwykle odkładanie na rezerwę,
                // a nie zwykłe przekładanie pieniędzy — ekran ma o to dopytać.
                paired && toAccount?.Kind == AccountKind.Savings,
                paired && fromAccount?.Kind == AccountKind.Savings
                    && toAccount?.Kind != AccountKind.Savings,
                paired));
        }

        return result;
    }
}
