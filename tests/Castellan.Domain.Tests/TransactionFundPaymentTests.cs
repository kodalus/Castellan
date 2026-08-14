using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Tests;

public class TransactionFundPaymentTests
{
    private static Transaction Expense(long grosze = -120_000) =>
        Transaction.CreateManual(
            AccountId.New(),
            new Money(grosze),
            DateTimeOffset.UtcNow,
            CategoryId.New());

    [Fact]
    public void Regular_expense_counts_toward_envelopes()
    {
        var tx = Expense();

        Assert.False(tx.IsExcludedFromCalculations);
    }

    [Fact]
    public void Expense_paid_from_fund_is_excluded_from_envelopes()
    {
        var tx = Expense();

        tx.PayFromFund(FundId.New());

        Assert.True(tx.IsExcludedFromCalculations);
    }

    [Fact]
    public void Clearing_fund_payment_brings_expense_back_into_envelopes()
    {
        var tx = Expense();
        tx.PayFromFund(FundId.New());

        tx.ClearFundPayment();

        Assert.Null(tx.PaidFromFundId);
        Assert.False(tx.IsExcludedFromCalculations);
    }

    [Fact]
    public void Fund_balance_drops_by_expense_and_returns_when_undone()
    {
        var fund = Fund.Create("OC", FundKind.Insurance, new Money(120_000),
            DateOnly.FromDateTime(DateTime.Today).AddMonths(6));
        fund.Contribute(new Money(120_000));

        fund.Withdraw(new Money(120_000));
        Assert.Equal(0, fund.Balance.Grosze);

        fund.Contribute(new Money(120_000));
        Assert.Equal(120_000, fund.Balance.Grosze);
    }
}
