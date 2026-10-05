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

    /// <summary>
    /// Banki, których powiadomienia aplikacja umie czytać. Lista jest krótka, bo każdy
    /// wpis to osobny parser napisany pod konkretny format wiadomości — nie da się tego
    /// dopisać samą nazwą.
    /// </summary>
    public static readonly string[] WithNotificationSupport = [Ing, Revolut];

    /// <summary>
    /// Banki do wyboru przy koncie. Ustawienie banku NIE włącza czytania powiadomień —
    /// to tylko etykieta, po której trafia na właściwe konto płatność zgłoszona przez
    /// Portfel Google („karta mBank Intensive"). Dla banku bez parsera jest więc nadal
    /// użyteczna, a dla kogoś, kto wpisuje wszystko ręcznie, jest zwykłym opisem.
    ///
    /// Nazwy są KRÓTKIE i celowo nie są pełnymi nazwami prawnymi: ta sama wartość służy
    /// do szukania banku w treści powiadomienia, a tam bank nazywa się „PKO", nie
    /// „Powszechna Kasa Oszczędności Bank Polski". Czego tu brakuje, wpisuje się ręcznie.
    /// </summary>
    public static readonly string[] Popular =
    [
        "Alior", "BNP Paribas", "BOŚ", "Citi", "Credit Agricole", Ing, "Inteligo",
        "mBank", "Millennium", "Nest", "Pekao", "PKO", Revolut, "Santander", "SGB",
        "VeloBank",
    ];
}

public enum CategoryKind { Expense, Income }

public enum TransactionSource { Manual, Notification, Reconciliation }

public enum TransactionKind { Regular, Authorization, Transfer, Unidentified }

public enum ParseStatus { Unparsed, Parsed, Ignored }

public enum CategoryRuleOrigin { Learned, Manual }

public enum FundKind { Insurance, Vacation, Tax, Custom, Emergency }

public enum AssetLiquidity { Immediate, Fast, Medium, Slow }

public enum DebtKind { Mortgage, CashLoan, Installment, FromFamily, Other }
