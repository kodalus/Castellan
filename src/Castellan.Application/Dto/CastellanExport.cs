namespace Castellan.Application.Dto;

public sealed class CastellanExport
{
    public int Version { get; init; } = 1;
    public string ExportedAt { get; init; } = "";
    public List<AccountDto> Accounts { get; init; } = [];
    public List<CategoryDto> Categories { get; init; } = [];
    public List<CategoryRuleDto> CategoryRules { get; init; } = [];
    public List<TransactionDto> Transactions { get; init; } = [];
    public List<MonthBudgetDto> MonthBudgets { get; init; } = [];
    public List<FundDto> Funds { get; init; } = [];
    public List<AssetDto> Assets { get; init; } = [];
}

public sealed record AccountDto(
    Guid Id, string Name, int Kind, int LiquidityTier,
    string? BankKey, bool IsArchived, long LastReconciledBalance, string LastReconciledAt);

public sealed record CategoryDto(
    Guid Id, string Name, int Kind, bool IsSystem, bool IsArchived);

public sealed record CategoryRuleDto(
    Guid Id, string Pattern, Guid CategoryId, string Origin, int HitCount, string? LastUsedAt);

public sealed record TransactionDto(
    Guid Id, Guid AccountId, long Amount, string OccurredAt, Guid CategoryId,
    string? RawMerchant, string? MerchantKey, string? Note, int Source, int Kind,
    Guid? TransferGroupId, Guid? ProposedTransferGroupId, Guid? SupersededById, Guid? RawNotificationId,
    Guid? PaidFromFundId = null);

public sealed record EnvelopeDto(Guid CategoryId, long PlannedAmount);

public sealed record MonthBudgetDto(
    Guid Id, string Month, long AvailableFunds, string PlannedAt,
    List<EnvelopeDto> Envelopes);

public sealed record FundDto(
    Guid Id, string Name, string Kind, long TargetAmount, string StartMonth,
    string Deadline, long Balance, bool IsArchived);

public sealed record AssetDto(
    Guid Id, string Name, string Liquidity, long Value, string UpdatedOn, bool IsArchived);
