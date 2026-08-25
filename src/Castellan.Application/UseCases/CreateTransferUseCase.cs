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
        FundId? ContributeTo = null);

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
        // dzięki czemu obie strony są wyłączone z kopert i ze statystyk przychodów.
        var groupId = Guid.NewGuid();
        incoming.SetTransferGroup(groupId);

        // Odkładanie na rezerwę zostawia nogę wychodzącą zwykłym wydatkiem w kategorii
        // „Rezerwy" — te pieniądze przestają być do wydania w tym miesiącu, więc muszą
        // obciążyć kopertę. Noga przychodząca i tak wypada z budżetu, inaczej ta sama
        // kwota wyszłaby jako przychód na koncie docelowym.
        var reserve = input.IsReserve
            ? (await categories.ListAsync(ct)).FirstOrDefault(c =>
                c.Name.Equals(ConfirmTransferUseCase.ReserveCategoryName, StringComparison.OrdinalIgnoreCase))
            : null;

        if (reserve is not null)
        {
            outgoing.MarkAsReserveMove(reserve.Id);

            if (input.ContributeTo is { } fundId)
            {
                var fund = await funds.GetAsync(fundId, ct);
                fund?.Contribute(new Money(magnitude));
            }
        }
        else
        {
            outgoing.SetTransferGroup(groupId);
        }

        await transactions.AddAsync(outgoing, ct);
        await transactions.AddAsync(incoming, ct);
        await uow.SaveChangesAsync(ct);
    }
}
