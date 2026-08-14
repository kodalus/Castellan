namespace Castellan.Domain;

public enum AccountKind { Checking, Savings }

public enum LiquidityTier { Immediate, Month, Locked }

public enum CategoryKind { Expense, Income }

public enum TransactionSource { Manual, Notification, Reconciliation }

public enum TransactionKind { Regular, Authorization, Transfer, Unidentified }

public enum ParseStatus { Unparsed, Parsed, Ignored }

public enum CategoryRuleOrigin { Learned, Manual }

public enum FundKind { Insurance, Vacation, Tax, Custom }

public enum AssetLiquidity { Immediate, Fast, Medium, Slow }
