namespace Castellan.Domain;

/// <summary>
/// Gotówka jest kontem, a nie wyjątkiem: portfel zachowuje się jak rachunek —
/// ma saldo, wychodzą z niego wydatki, da się go uzgodnić przeliczeniem zawartości,
/// a odłożenie pieniędzy do szuflady jest przelewem, nie zniknięciem.
///
/// Osobny typ, a nie „rachunek bieżący z nazwą Gotówka", z dwóch powodów: lista kont
/// przestaje kłamać, a portfel wypada z awaryjnego wyboru konta przy powiadomieniach —
/// żaden bank nie powiadamia o gotówce, więc cudza płatność nie ma prawa tam wylądować.
/// </summary>
public enum AccountKind { Checking, Savings, Cash }

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

public enum CategoryKind { Expense, Income }

public enum TransactionSource { Manual, Notification, Reconciliation }

public enum TransactionKind { Regular, Authorization, Transfer, Unidentified }

public enum ParseStatus { Unparsed, Parsed, Ignored }

public enum CategoryRuleOrigin { Learned, Manual }

public enum FundKind { Insurance, Vacation, Tax, Custom, Emergency }

public enum AssetLiquidity { Immediate, Fast, Medium, Slow }

public enum DebtKind { Mortgage, CashLoan, Installment, FromFamily, Other }
