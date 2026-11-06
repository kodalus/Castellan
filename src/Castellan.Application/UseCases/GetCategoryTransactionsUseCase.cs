using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record CategoryTransactionRow(
    TransactionId Id,
    DateTimeOffset OccurredAt,
    Money Amount,
    string? Merchant,
    string? Note,
    string AccountName,
    /// <summary>Zwrot kosztów — kwota dodatnia w kategorii wydatkowej, pomniejsza sumę.</summary>
    bool IsRefund);

public sealed record CategoryTransactions(
    string CategoryName,
    YearMonth From,
    YearMonth To,
    Money Total,
    IReadOnlyList<CategoryTransactionRow> Items)
{
    public bool IsEmpty => Items.Count == 0;
}

/// <summary>
/// Transakcje jednej kategorii za wybrany okres — rozwinięcie liczby ze Statystyk.
///
/// Definicja jest DOKŁADNIE ta sama co w <see cref="GetMonthlyStatsUseCase"/>: te same
/// wykluczenia i ten sam sposób liczenia zwrotów. Gdyby się rozjechały, suma pod listą
/// nie zgadzałaby się z kwotą, w którą użytkownik przed chwilą tapnął — a wtedy nie ma
/// jak dojść, która z dwóch liczb kłamie.
/// </summary>
public sealed class GetCategoryTransactionsUseCase(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts)
{
    public async Task<CategoryTransactions> ExecuteAsync(
        CategoryId categoryId, YearMonth upTo, int monthCount = 1, CancellationToken ct = default)
    {
        var from = upTo;
        for (var i = 0; i < monthCount - 1; i++) from = from.Previous();

        var cats = await categories.ListAsync(ct);
        var catMap = cats.ToDictionary(c => c.Id);
        var name = catMap.TryGetValue(categoryId, out var cat) ? cat.Name : "?";

        var accountMap = (await accounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.Name);

        var rows = new List<CategoryTransactionRow>();
        var total = 0L;

        var current = from;
        while (current.CompareTo(upTo) <= 0)
        {
            foreach (var tx in await transactions.ListForMonthAsync(current, ct))
            {
                if (tx.IsExcludedFromCalculations || tx.CategoryId != categoryId) continue;

                var isRefund = RefundAccounting.IsRefund(tx, catMap);
                // Przychód w kategorii wydatkowej, który nie jest zwrotem, nie ma tu
                // czego robić — tak samo jak nie wchodzi do sumy w Statystykach.
                if (!tx.Amount.IsNegative && !isRefund) continue;

                total += RefundAccounting.NetExpenseGrosze(tx, catMap);
                rows.Add(new CategoryTransactionRow(
                    tx.Id,
                    tx.OccurredAt,
                    tx.Amount,
                    tx.RawMerchant,
                    tx.Note,
                    accountMap.GetValueOrDefault(tx.AccountId, "?"),
                    isRefund));
            }
            current = current.Next();
        }

        // Od najnowszej: pytanie „na co poszło tyle pieniędzy" zaczyna się od tego,
        // co pamiętasz najlepiej.
        rows.Sort((a, b) => b.OccurredAt.CompareTo(a.OccurredAt));

        return new CategoryTransactions(name, from, upTo, new Money(total), rows);
    }
}
