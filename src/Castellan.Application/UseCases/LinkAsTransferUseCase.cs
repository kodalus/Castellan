using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record TransferCandidate(
    TransactionId Id,
    AccountId AccountId,
    string AccountName,
    Money Amount,
    DateTimeOffset OccurredAt,
    string? Merchant);

/// <summary>
/// Kandydaci na drugą stronę przelewu dla istniejącego wpisu: przeciwna kwota co do
/// grosza, w oknie 48 godzin, jeszcze niesparowane.
///
/// Konto NIE jest tu warunkiem — inaczej niż przy wykrywaniu przelewu z powiadomień.
/// Cała potrzeba bierze się bowiem z sytuacji, w której obie nogi wylądowały na TYM
/// SAMYM koncie, bo powiadomienia banku nie mówiły, którego konta dotyczą.
/// </summary>
public sealed class GetTransferCandidatesUseCase(
    ITransactionRepository transactions,
    IAccountRepository accounts)
{
    private const int WindowHours = 48;

    public async Task<IReadOnlyList<TransferCandidate>> ExecuteAsync(
        TransactionId id, CancellationToken ct = default)
    {
        var source = await transactions.GetAsync(id, ct);
        if (source is null || source.Amount.Grosze == 0) return [];

        var recent = await transactions.ListRecentAsync(source.OccurredAt.AddHours(-WindowHours), ct);
        var names = (await accounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.Name);

        return
        [
            .. recent
                .Where(t => t.Id != source.Id
                         && t.Amount.Grosze == -source.Amount.Grosze
                         && t.TransferGroupId is null
                         && t.ProposedTransferGroupId is null
                         && !t.SupersededById.HasValue
                         && Math.Abs((t.OccurredAt - source.OccurredAt).TotalHours) <= WindowHours)
                .OrderBy(t => Math.Abs((t.OccurredAt - source.OccurredAt).Ticks))
                .Select(t => new TransferCandidate(
                    t.Id, t.AccountId, names.GetValueOrDefault(t.AccountId, "?"),
                    t.Amount, t.OccurredAt, t.RawMerchant)),
        ];
    }
}

public enum LinkTransferResult
{
    Linked,
    NotFound,
    AlreadyLinked,
    NotOpposite,
    SameAccount,
}

/// <summary>
/// Łączy dwa istniejące wpisy w jeden przelew. Powstało z realnej ścieżki ratunkowej:
/// przy przelewie między kontami tego samego banku powiadomienia nie mówią, którego
/// konta dotyczą, więc obie nogi lądują na jednym koncie — jedna z plusem, druga
/// z minusem. Do tej pory jedynym wyjściem było skasowanie obu i wpisanie przelewu
/// ręcznie, czyli wyrzucenie tego, co aplikacja już wiedziała.
///
/// <paramref name="moveIncomingTo"/> pozwala przy okazji przenieść nogę wchodzącą na
/// właściwe konto. Bez tego para na jednym koncie nie ma sensu: przelew z definicji
/// łączy DWA konta, a wpis, który znosi sam siebie, byłby gorszy niż brak wpisu.
/// </summary>
public sealed class LinkAsTransferUseCase(
    ITransactionRepository transactions,
    IAccountRepository accounts,
    IUnitOfWork uow)
{
    public async Task<LinkTransferResult> ExecuteAsync(
        TransactionId firstId,
        TransactionId secondId,
        AccountId? moveIncomingTo = null,
        CancellationToken ct = default)
    {
        if (firstId == secondId) return LinkTransferResult.NotFound;

        var first = await transactions.GetAsync(firstId, ct);
        var second = await transactions.GetAsync(secondId, ct);
        if (first is null || second is null) return LinkTransferResult.NotFound;

        if (first.TransferGroupId is not null || second.TransferGroupId is not null
            || first.ProposedTransferGroupId is not null || second.ProposedTransferGroupId is not null)
            return LinkTransferResult.AlreadyLinked;

        if (first.Amount.Grosze == 0 || first.Amount.Grosze != -second.Amount.Grosze)
            return LinkTransferResult.NotOpposite;

        var incoming = first.Amount.IsNegative ? second : first;
        var outgoing = first.Amount.IsNegative ? first : second;

        // Wszystkie sprawdzenia PRZED jakąkolwiek zmianą. Inaczej odmowa z powodu tego
        // samego konta zostawiałaby już przestawione konto na śledzonym obiekcie —
        // niezapisane, ale gotowe pojechać przy najbliższym zapisie czegokolwiek innego.
        if (moveIncomingTo is { } target)
        {
            var account = await accounts.GetAsync(target, ct);
            if (account is null || account.IsArchived) return LinkTransferResult.NotFound;
        }

        var incomingAccount = moveIncomingTo ?? incoming.AccountId;
        if (outgoing.AccountId == incomingAccount) return LinkTransferResult.SameAccount;

        if (moveIncomingTo is { } destination) incoming.SetAccount(destination);

        var groupId = Guid.NewGuid();
        first.SetTransferGroup(groupId);
        second.SetTransferGroup(groupId);

        await uow.SaveChangesAsync(ct);
        return LinkTransferResult.Linked;
    }
}
