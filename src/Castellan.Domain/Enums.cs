namespace Castellan.Domain;

public enum AccountKind { Checking, Savings }

/// <summary>
/// Banki, których powiadomienia aplikacja umie czytać. Ta sama lista służy do dwóch
/// rzeczy: do wyboru banku przy koncie i do rozpoznania, z którego banku przyszło
/// powiadomienie — muszą być identyczne, inaczej wybór użytkownika nic by nie znaczył.
/// </summary>
public static class Banks
{
    public const string Ing = "ING";
    public const string Revolut = "Revolut";

    public static readonly string[] Known = [Ing, Revolut];
}

public enum LiquidityTier { Immediate, Month, Locked }

public enum CategoryKind { Expense, Income }

public enum TransactionSource { Manual, Notification, Reconciliation }

public enum TransactionKind { Regular, Authorization, Transfer, Unidentified }

public enum ParseStatus { Unparsed, Parsed, Ignored }

public enum CategoryRuleOrigin { Learned, Manual }

public enum FundKind { Insurance, Vacation, Tax, Custom, Emergency }

public enum AssetLiquidity { Immediate, Fast, Medium, Slow }

public enum DebtKind { Mortgage, CashLoan, Installment, FromFamily, Other }
