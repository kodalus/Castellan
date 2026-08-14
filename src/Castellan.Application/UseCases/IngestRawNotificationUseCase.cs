using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.UseCases;

public sealed partial class IngestRawNotificationUseCase(
    IRawNotificationRepository rawNotifications,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ICategoryRuleRepository categoryRules,
    IUnitOfWork uow,
    IEnumerable<INotificationParser> parsers)
{
    public static readonly IReadOnlySet<string> AllowedPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        "pl.ing.mojeing",
        "com.revolut.revolut",
        "com.google.android.apps.walletnfcrel",
    };

    private static readonly IReadOnlySet<string> IgnoredPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        "com.google.android.apps.walletnfcrel",
    };

    public sealed record Input(string PackageName, string Title, string Text, DateTimeOffset PostedAt);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        if (!AllowedPackages.Contains(input.PackageName)) return;

        var maskedTitle = MaskSensitiveData(input.Title);
        var maskedText  = MaskSensitiveData(input.Text);

        var notification = IgnoredPackages.Contains(input.PackageName)
            ? RawNotification.CreateIgnored(input.PackageName, maskedTitle, maskedText, input.PostedAt)
            : RawNotification.CreateUnparsed(input.PackageName, maskedTitle, maskedText, input.PostedAt);

        await rawNotifications.AddAsync(notification, ct);

        if (notification.ParseStatus == ParseStatus.Unparsed)
        {
            var rules = await categoryRules.ListAsync(ct);
            await TryAutoParseAsync(notification, input.PackageName, input.PostedAt, rules, ct);
        }

        await uow.SaveChangesAsync(ct);
    }

    private async Task TryAutoParseAsync(
        RawNotification notification,
        string packageName,
        DateTimeOffset postedAt,
        IReadOnlyList<CategoryRule> rules,
        CancellationToken ct)
    {
        var parser = parsers.FirstOrDefault(p => p.PackageName == packageName);
        if (parser is null) return;

        var parsed = parser.TryParse(notification.Title, notification.Text);
        if (parsed is null) return;

        var account = await FindAccountAsync(packageName, ct);
        if (account is null) return;

        var tx = Transaction.CreateFromNotification(
            account.Id, parsed.Amount, postedAt, notification.Id, parsed.Merchant);

        // Normalize and set MerchantKey
        var merchantKey = MerchantKeyNormalizer.Normalize(parsed.Merchant);
        tx.SetMerchantKey(merchantKey);

        // Apply best matching rule: longest pattern wins, tie-break by HitCount
        var matchText = merchantKey ?? parsed.Merchant;
        var matchedRule = rules
            .Where(r => r.Matches(matchText))
            .OrderByDescending(r => r.Pattern.Length)
            .ThenByDescending(r => r.HitCount)
            .FirstOrDefault();

        if (matchedRule is not null)
        {
            tx.AssignCategory(matchedRule.CategoryId);
            matchedRule.RecordHit();
        }

        // Deduplication check (spec 11.1)
        var dedupResult = await TryDeduplicateAsync(tx, account.Id, postedAt, merchantKey, ct);
        if (dedupResult == DeduplicateResult.ExactDuplicate)
        {
            // Drop silently — notification still marked as parsed below
            notification.MarkParsed(tx.Id);
            return;
        }

        await transactions.AddAsync(tx, ct);

        // Transfer detection (spec 11.2) — propose after adding tx so it's reachable
        await TryProposeTransferAsync(tx, account.Id, postedAt, ct);

        notification.MarkParsed(tx.Id);
    }

    private enum DeduplicateResult { None, Authorization, ExactDuplicate }

    private async Task<DeduplicateResult> TryDeduplicateAsync(
        Transaction tx,
        AccountId accountId,
        DateTimeOffset postedAt,
        string? merchantKey,
        CancellationToken ct)
    {
        if (merchantKey is null) return DeduplicateResult.None;

        var since = postedAt.AddHours(-25);
        var recent = await transactions.ListRecentAsync(since, ct);

        var candidate = recent.FirstOrDefault(t =>
            t.AccountId == accountId &&
            !t.SupersededById.HasValue &&
            t.MerchantKey is not null &&
            t.MerchantKey.Equals(merchantKey, StringComparison.OrdinalIgnoreCase) &&
            AmountMatches(t.Amount.Grosze, tx.Amount.Grosze));

        if (candidate is null) return DeduplicateResult.None;

        if (candidate.Kind == TransactionKind.Authorization)
        {
            // Authorization pre-auth → supersede it with the real charge
            candidate.Supersede(tx.Id);
            return DeduplicateResult.Authorization;
        }

        // Same kind — this is an exact duplicate; don't add it
        return DeduplicateResult.ExactDuplicate;
    }

    private static bool AmountMatches(long a, long b)
    {
        if (a == b) return true;
        if (b == 0) return a == 0;
        return Math.Abs(a - b) <= Math.Abs(b) * 2 / 100; // ≤2% diff
    }

    private async Task TryProposeTransferAsync(
        Transaction tx,
        AccountId ownAccountId,
        DateTimeOffset postedAt,
        CancellationToken ct)
    {
        var since = postedAt.AddHours(-48);
        var recent = await transactions.ListRecentAsync(since, ct);

        var match = recent.FirstOrDefault(t =>
            t.AccountId != ownAccountId &&
            t.Amount.Grosze == -tx.Amount.Grosze &&
            t.Kind != TransactionKind.Transfer &&
            t.ProposedTransferGroupId is null &&
            !t.SupersededById.HasValue &&
            t.Id != tx.Id);

        if (match is null) return;

        var groupId = Guid.NewGuid();
        tx.ProposeTransfer(groupId);
        match.ProposeTransfer(groupId);
    }

    private async Task<Account?> FindAccountAsync(string packageName, CancellationToken ct)
    {
        var all = await accounts.ListAsync(ct);
        var active = all.Where(a => !a.IsArchived).ToList();

        var keyword = packageName switch
        {
            "pl.ing.mojeing"      => "ING",
            "com.revolut.revolut" => "Revolut",
            _                     => null,
        };

        if (keyword is not null)
        {
            var byName = active.FirstOrDefault(a =>
                a.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }

        return active.FirstOrDefault(a => a.Kind == AccountKind.Checking)
            ?? active.FirstOrDefault();
    }

    private static string MaskSensitiveData(string text) =>
        SensitivePattern().Replace(text, "****");

    [GeneratedRegex(@"(?<![,.\d])\b\d{4,8}\b(?![,.\d])")]
    private static partial Regex SensitivePattern();
}
