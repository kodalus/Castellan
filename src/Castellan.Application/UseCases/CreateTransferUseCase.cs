using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

/// <summary>
/// Ręczny przelew między własnymi kontami. Automatyczne wykrywanie z powiadomień
/// wymaga, żeby dotarły OBA — obciążenie konta źródłowego i wpływ na docelowe.
/// Gdy któregoś zabraknie (np. bank nie powiadamia o przelewie wychodzącym),
/// wpływ zostałby policzony jako przychód i zawyżył budżet. Ten use case zapisuje
/// obie strony naraz, oznaczone jako jedna para.
/// </summary>
public sealed class CreateTransferUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IFundRepository funds,
    IUnitOfWork uow)
{
    public sealed record Input(
        AccountId FromAccountId,
        AccountId ToAccountId,
        Money Amount,
        DateTimeOffset OccurredAt,
        string? Note = null,
        /// <summary>Odkładanie na rezerwę, a nie zwykłe przekładanie pieniędzy.</summary>
        bool IsReserve = false,
        /// <summary>Fundusz, którego saldo ma wzrosnąć. Dobrowolny nawet przy rezerwie.</summary>
        FundId? ContributeTo = null,
        /// <summary>
        /// Fundusz, z którego te pieniądze są wyjmowane. Odkładanie się kończy, więc saldo
        /// funduszu maleje. Dobrowolny: część rezerw nie ma przypisanego celu.
        /// </summary>
        FundId? WithdrawFrom = null,
        /// <summary>
        /// Wyjęcie odłożonych pieniędzy, a nie zwykłe przekładanie — odwrotność
        /// <see cref="IsReserve"/>. Osobna flaga, a nie samo <see cref="WithdrawFrom"/>,
        /// bo wskazanie funduszu jest dobrowolne: „wyjmuję z rezerwy, ale bez konkretnego
        /// celu" musi dawać się odróżnić od „przekładam między kontami".
        /// </summary>
        bool IsReserveReturn = false);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        if (input.FromAccountId == input.ToAccountId)
            throw new InvalidOperationException("Konto źródłowe i docelowe muszą być różne.");

        var magnitude = Math.Abs(input.Amount.Grosze);
        if (magnitude == 0)
            throw new InvalidOperationException("Kwota przelewu musi być większa od zera.");

        var from = await accounts.GetAsync(input.FromAccountId, ct)
            ?? throw new InvalidOperationException($"Account {input.FromAccountId} not found.");
        var to = await accounts.GetAsync(input.ToAccountId, ct)
            ?? throw new InvalidOperationException($"Account {input.ToAccountId} not found.");

        if (from.IsArchived || to.IsArchived)
            throw new InvalidOperationException("Nie można zrobić przelewu na zarchiwizowane konto.");

        var outgoing = Transaction.CreateManual(
            input.FromAccountId, new Money(-magnitude), input.OccurredAt, Category.TransferId, input.Note);
        var incoming = Transaction.CreateManual(
            input.ToAccountId, new Money(magnitude), input.OccurredAt, Category.TransferId, input.Note);

        // Wspólny TransferGroupId ustawia też Kind=Transfer i kategorię systemową,
        // dzięki czemu dana strona jest wyłączona z kopert i ze statystyk przychodów.
        var groupId = Guid.NewGuid();

        // Rezerwa działa w obie strony i w obie strony dotyka tej samej koperty.
        // Przy odkładaniu w „Rezerwach" ląduje noga WYCHODZĄCA (kwota ujemna): pieniądze
        // przestają być do wydania w tym miesiącu, więc muszą obciążyć kopertę.
        // Przy wyjmowaniu — noga PRZYCHODZĄCA (kwota dodatnia): wracają do wydania
        // w miesiącu, w którym je wyjęto, więc koperta musi o tyle zmaleć.
        //
        // Druga noga wypada z budżetu jak każdy przelew, inaczej ta sama kwota wyszłaby
        // drugi raz — jako przychód albo jako wydatek na drugim koncie.
        var reserve = input.IsReserve || input.IsReserveReturn
            ? (await categories.ListAsync(ct)).FirstOrDefault(c =>
                c.Name.Equals(ConfirmTransferUseCase.ReserveCategoryName, StringComparison.OrdinalIgnoreCase))
            : null;

        if (reserve is not null && input.IsReserve)
        {
            outgoing.MarkAsReserveMove(reserve.Id);
            incoming.SetTransferGroup(groupId);

            if (input.ContributeTo is { } fundId)
            {
                var fund = await funds.GetAsync(fundId, ct);
                fund?.Contribute(new Money(magnitude));
            }
        }
        else if (reserve is not null && input.IsReserveReturn)
        {
            outgoing.SetTransferGroup(groupId);
            incoming.MarkAsReserveMove(reserve.Id);
        }
        else
        {
            outgoing.SetTransferGroup(groupId);
            incoming.SetTransferGroup(groupId);
        }

        if (input.WithdrawFrom is { } withdrawId)
        {
            var fund = await funds.GetAsync(withdrawId, ct);
            fund?.Withdraw(new Money(magnitude));
        }

        await transactions.AddAsync(outgoing, ct);
        await transactions.AddAsync(incoming, ct);
        await uow.SaveChangesAsync(ct);
    }
}
