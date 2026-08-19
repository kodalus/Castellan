namespace Castellan.Domain;

public enum AccountKind { Checking, Savings }

public enum LiquidityTier { Immediate, Month, Locked }

public enum CategoryKind { Expense, Income }

public enum TransactionSource { Manual, Notification, Reconciliation }

public enum TransactionKind { Regular, Authorization, Transfer, Unidentified }

public enum ParseStatus { Unparsed, Parsed, Ignored }

public enum CategoryRuleOrigin { Learned, Manual }

// Emergency stoi osobno od reszty, bo jako jedyny liczy się do poduszki finansowej
// w Majątku — pozostałe fundusze mają już przypisany konkretny przyszły wydatek,
// więc nie są rezerwą na czarną godzinę.
public enum FundKind { Insurance, Vacation, Tax, Custom, Emergency }

public enum AssetLiquidity { Immediate, Fast, Medium, Slow }

public enum DebtKind { Mortgage, CashLoan, Installment, FromFamily, Other }
