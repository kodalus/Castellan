using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed class ContributeToFundUseCase(
    IFundRepository funds,
    ICategoryRepository categories,
    ITransactionRepository transactions,
    IUnitOfWork uow)
{
    /// <summary>
    /// Podnosi saldo funduszu. Gdy podane jest konto, dopisuje też wydatek w kategorii
    /// „Rezerwy" — bez niego wpłata nie zostawiała w budżecie żadnego śladu i koperta
    /// Rezerwy pokazywała zero wydanych, mimo że pieniądze poszły.
    ///
    /// Konto jest opcjonalne, bo istnieje druga droga: wydatek w kategorii „Rezerwy"
    /// zapisany wcześniej, który dopiero PYTA o fundusz. Tam transakcja już jest
    /// i drugi zapis liczyłby tę samą kwotę dwa razy.
    /// </summary>
    public async Task ExecuteAsync(
        FundId id,
        Money amount,
        AccountId? fromAccount = null,
        CancellationToken ct = default)
    {
        var fund = await funds.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Fund {id} not found.");
        fund.Contribute(amount);

        if (fromAccount is { } accountId)
        {
            var reserve = (await categories.ListAsync(ct))
                .FirstOrDefault(c => c.Name.Equals(ConfirmTransferUseCase.ReserveCategoryName,
                    StringComparison.OrdinalIgnoreCase));

            if (reserve is not null)
            {
                var tx = Transaction.CreateManual(
                    accountId,
                    new Money(-Math.Abs(amount.Grosze)),
                    DateTimeOffset.Now,
                    reserve.Id);
                tx.SetNote($"Wpłata na fundusz: {fund.Name}");
                await transactions.AddAsync(tx, ct);
            }
        }

        await uow.SaveChangesAsync(ct);
    }
}
