using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Transaction
{
    public TransactionId Id { get; private set; }
    public AccountId AccountId { get; private set; }
    public Money Amount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public CategoryId CategoryId { get; private set; }
    public string? RawMerchant { get; private set; }
    public string? MerchantKey { get; private set; }
    public string? Note { get; private set; }
    public TransactionSource Source { get; private set; }
    public TransactionKind Kind { get; private set; }
    public Guid? TransferGroupId { get; private set; }
    public Guid? ProposedTransferGroupId { get; private set; }
    public TransactionId? SupersededById { get; private set; }
    public Guid? RawNotificationId { get; private set; }

    // Wydatek pokryty z funduszu — odpisy trafiły do kopert w poprzednich
    // miesiącach, więc sama zapłata nie może obciążać budżetu ponownie.
    public FundId? PaidFromFundId { get; private set; }

    /// <summary>
    /// Fundusz, którego saldo ruszyła ta transakcja przy odkładaniu albo wyjmowaniu.
    ///
    /// Saldo funduszu jest osobną, zapisaną liczbą — nie wynika z transakcji. Bez tego
    /// powiązania nic nie wiedziało, że skasowanie wpisu ma je cofnąć: usunięty przelew
    /// zostawiał podniesione saldo funduszu i jedynym ratunkiem była ręczna poprawka.
    ///
    /// Kierunek mówi znak kwoty samej transakcji, bo tak właśnie działa odkładanie:
    /// pieniądze wychodzące z konta do wydawania (kwota ujemna) poszły DO funduszu,
    /// a przychodzące (dodatnia) z niego wyszły.
    /// </summary>
    public FundId? ReserveFundId { get; private set; }

    // N-4: superseded or transfer excluded from calculations
    public bool IsExcludedFromCalculations =>
        Kind == TransactionKind.Transfer || SupersededById.HasValue || PaidFromFundId.HasValue;

    private Transaction() { }

    // N-2: CategoryId always set — defaults to Unsorted when called from notification path
    public static Transaction CreateManual(
        AccountId accountId,
        Money amount,
        DateTimeOffset occurredAt,
        CategoryId categoryId,
        string? note = null)
    {
        return new Transaction
        {
            Id = TransactionId.New(),
            AccountId = accountId,
            Amount = amount,
            OccurredAt = occurredAt,
            CategoryId = categoryId,
            Note = note,
            Source = TransactionSource.Manual,
            Kind = TransactionKind.Regular,
        };
    }

    public static Transaction CreateFromNotification(
        AccountId accountId,
        Money amount,
        DateTimeOffset occurredAt,
        RawNotificationId rawNotificationId,
        string? merchant = null)
    {
        return new Transaction
        {
            Id = TransactionId.New(),
            AccountId = accountId,
            Amount = amount,
            OccurredAt = occurredAt,
            CategoryId = Category.UnsortedId,
            RawMerchant = merchant,
            Note = merchant,
            RawNotificationId = rawNotificationId.Value,
            Source = TransactionSource.Notification,
            Kind = TransactionKind.Regular,
        };
    }

    // N-2, N-6: creates Unidentified transaction for negative reconciliation discrepancy
    public static Transaction CreateReconciliation(
        AccountId accountId,
        Money discrepancy,
        DateTimeOffset occurredAt)
    {
        return new Transaction
        {
            Id = TransactionId.New(),
            AccountId = accountId,
            Amount = discrepancy,
            OccurredAt = occurredAt,
            CategoryId = Category.UnidentifiedId,
            Source = TransactionSource.Reconciliation,
            Kind = TransactionKind.Unidentified,
        };
    }

    public void AssignCategory(CategoryId categoryId) => CategoryId = categoryId;

    public void SetMerchantKey(string? key) => MerchantKey = key;

    public void SetNote(string? note) => Note = note;

    public void SetAmount(Money amount) => Amount = amount;

    public void SetAccount(AccountId accountId) => AccountId = accountId;

    public void SetOccurredAt(DateTimeOffset occurredAt) => OccurredAt = occurredAt;

    public void Supersede(TransactionId byId) => SupersededById = byId;

    public void SetTransferGroup(Guid groupId)
    {
        TransferGroupId = groupId;
        ProposedTransferGroupId = null;
        Kind = TransactionKind.Transfer;
        CategoryId = Category.TransferId;
    }

    /// <summary>
    /// Przelew na własne konto oszczędnościowe to jedyny przelew, który OBCIĄŻA budżet
    /// miesiąca: te pieniądze przestają być do wydania, dokładnie tak jak zakup. Noga
    /// wychodząca zostaje więc zwykłym wydatkiem w kategorii „Rezerwy" zamiast zniknąć
    /// jako przelew — bez tego koperta Rezerwy nigdy nie pokazywała ani grosza wydanego
    /// i była planem, którego nie dało się z niczym porównać.
    ///
    /// Noga przychodząca jest wykluczana jak każdy przelew (SetTransferGroup) — inaczej
    /// ta sama kwota wyszłaby jako przychód na koncie docelowym.
    ///
    /// TO SAMO DZIAŁA W DRUGĄ STRONĘ. Gdy odłożone pieniądze wracają z oszczędności do
    /// wydawania, nogą w kategorii „Rezerwy” jest ta PRZYCHODZĄCA — z kwotą dodatnią,
    /// więc koperta Rezerwy maleje o tyle, ile zabrano. Operacja jest identyczna,
    /// kierunek rozstrzyga sam znak kwoty.
    ///
    /// <paramref name="transferGroupId"/> spina tę nogę z drugą, NIE zmieniając jej
    /// rodzaju ani kategorii — na tym polega różnica wobec <see cref="SetTransferGroup"/>,
    /// które zamieniłoby ją w zwykły przelew i wypchnęło z budżetu. Bez tego spięcia obie
    /// nogi były dla aplikacji niezwiązanymi wpisami: skasowanie jednej zostawiało drugą
    /// samą na swoim koncie, więc saldo jednego konta wracało do stanu sprzed przelewu,
    /// a drugiego nie.
    /// </summary>
    public void MarkAsReserveMove(CategoryId reserveCategory, Guid? transferGroupId = null)
    {
        ProposedTransferGroupId = null;
        CategoryId = reserveCategory;
        // Zwykly wydatek, nie przelew — inaczej wypadlby z budzetu i koperta „Rezerwy"
        // znow nie pokazywalaby ani grosza. Przy tworzeniu nogi to i tak jej stan,
        // ale przy ZMIANIE rodzaju istniejacego przelewu trzeba go cofnac.
        Kind = TransactionKind.Regular;
        if (transferGroupId is { } groupId) TransferGroupId = groupId;
    }

    public void PayFromFund(FundId fundId) => PaidFromFundId = fundId;

    public void SetReserveFund(FundId fundId) => ReserveFundId = fundId;

    public void ClearReserveFund() => ReserveFundId = null;

    public void ClearFundPayment() => PaidFromFundId = null;

    public void ProposeTransfer(Guid proposalGroupId) =>
        ProposedTransferGroupId = proposalGroupId;

    public void ClearTransferProposal() =>
        ProposedTransferGroupId = null;
}
